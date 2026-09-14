using DataBoard.Application.Dashboards;
using DataBoard.Domain.Dashboards;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DashboardRepository(AppDbContext db) : IDashboardRepository
{
    public async Task AddAsync(Dashboard dashboard, CancellationToken ct = default) =>
        await db.Dashboards.AddAsync(dashboard, ct);

    public void Remove(Dashboard dashboard) => db.Dashboards.Remove(dashboard);

    public Task<Dashboard?> GetAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default) =>
        db.Dashboards.SingleOrDefaultAsync(d => d.OrganizationId == organizationId && d.Id == dashboardId, ct);

    public async Task<IReadOnlyList<Dashboard>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        await db.Dashboards.AsNoTracking().Where(d => d.OrganizationId == organizationId).OrderBy(d => d.Name).ToListAsync(ct);

    public Task<int> CountScheduledAsync(Guid organizationId, CancellationToken ct = default) =>
        db.Dashboards.CountAsync(d => d.OrganizationId == organizationId && d.RefreshIntervalMinutes != null, ct);

    public async Task<IReadOnlyList<DueDashboard>> ClaimDueAsync(DateTime nowUtc, int max, CancellationToken ct = default)
    {
        // One statement: pick the due rows, skipping any another server has locked right now, and move their next due time on.
        const string sql = """
            UPDATE dashboards
            SET next_refresh_utc = @now + (refresh_interval_minutes * interval '1 minute')
            WHERE "Id" IN (
                SELECT "Id" FROM dashboards
                WHERE refresh_interval_minutes IS NOT NULL AND next_refresh_utc <= @now
                ORDER BY next_refresh_utc
                LIMIT @max
                FOR UPDATE SKIP LOCKED)
            RETURNING "Id", "OrganizationId"
            """;

        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) await connection.OpenAsync(ct);
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.Add(new Npgsql.NpgsqlParameter("now", DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)));
            command.Parameters.Add(new Npgsql.NpgsqlParameter("max", max));

            var due = new List<DueDashboard>();
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) due.Add(new DueDashboard(reader.GetGuid(0), reader.GetGuid(1)));
            return due;
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }

    public Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeDashboardId, CancellationToken ct = default)
    {
        var lower = name.ToLowerInvariant();
        return db.Dashboards.AnyAsync(
            d => d.OrganizationId == organizationId && d.Name.ToLower() == lower && d.Id != excludeDashboardId, ct);
    }
}
