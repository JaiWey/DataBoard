using DataBoard.Application.Abstractions;
using DataBoard.Application.Charts;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Application.Identity;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

internal sealed class FakeDataSourceRepo : IDataSourceRepository
{
    public List<DataSource> Items { get; } = [];
    public Task AddAsync(DataSource d, CancellationToken ct = default) { Items.Add(d); return Task.CompletedTask; }
    public Task<IReadOnlyList<DataSource>> ListByOrganizationAsync(Guid org, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DataSource>>(Items.Where(i => i.OrganizationId == org).ToList());
    public Task<DataSource?> GetAsync(Guid organizationId, Guid dataSourceId, CancellationToken ct = default) =>
        Task.FromResult(Items.SingleOrDefault(i => i.OrganizationId == organizationId && i.Id == dataSourceId));
}

internal sealed class FakeConnector : IDataSourceConnector
{
    public DataSourceType Type => DataSourceType.PostgreSql;
    public List<string> ValidatedConnectionStrings { get; } = [];
    public List<string> UsedConnectionStrings { get; } = [];
    public string? LastSql { get; private set; }
    public int? LastMaxRows { get; private set; }
    public Exception? ValidationFailure { get; set; }

    public void ValidateConnectionString(string connectionString)
    {
        ValidatedConnectionStrings.Add(connectionString);
        if (ValidationFailure is not null) throw ValidationFailure;
    }

    public Task<ConnectionTestResult> TestAsync(string connectionString, CancellationToken ct = default)
    {
        UsedConnectionStrings.Add(connectionString);
        return Task.FromResult(new ConnectionTestResult(true, "ok", "17", 1));
    }

    public Task<DatabaseSchema> GetSchemaAsync(string connectionString, CancellationToken ct = default)
    {
        UsedConnectionStrings.Add(connectionString);
        return Task.FromResult(Schema);
    }

    public DatabaseSchema Schema { get; set; } = new([new SchemaNode("public", [])], false);

    public ParameterizedQuery? LastQuery { get; private set; }
    public int? LastExecuteMaxRows { get; private set; }

    public Task<QueryPreview> ExecuteAsync(string connectionString, ParameterizedQuery query, int maxRows, CancellationToken ct = default)
    {
        UsedConnectionStrings.Add(connectionString);
        LastQuery = query;
        LastExecuteMaxRows = maxRows;
        return Task.FromResult(new QueryPreview([new("n", "bigint")], [[3L]], false));
    }

    public List<PreviewColumn> DescribedColumns { get; set; } = [new("id", "integer"), new("name", "text")];
    public int DescribeCalls { get; private set; }
    public int PreviewCalls { get; private set; }

    /// <summary>The values the last describe or preview was asked to bind.</summary>
    public IReadOnlyList<object?> LastBound { get; private set; } = [];

    public Task<IReadOnlyList<PreviewColumn>> DescribeAsync(
        string connectionString, string validatedSql, IReadOnlyList<object?> parameters, CancellationToken ct = default)
    {
        DescribeCalls++;
        UsedConnectionStrings.Add(connectionString);
        LastSql = validatedSql;
        LastBound = parameters;
        return Task.FromResult<IReadOnlyList<PreviewColumn>>(DescribedColumns);
    }

    public Task<QueryPreview> PreviewAsync(
        string connectionString, string validatedSql, int maxRows, IReadOnlyList<object?> parameters, CancellationToken ct = default)
    {
        PreviewCalls++;
        UsedConnectionStrings.Add(connectionString);
        LastSql = validatedSql;
        LastBound = parameters;
        LastMaxRows = maxRows;
        return Task.FromResult(new QueryPreview([], [], false));
    }
}

internal sealed class FakeValidator : ISqlQueryValidator
{
    public bool Reject { get; set; }

    public string ValidateReadOnly(DataSourceType type, string sql) =>
        Reject ? throw new Common.ValidationException("Sql", "rejected")
        : sql.StartsWith("VALIDATED:", StringComparison.Ordinal) ? sql : "VALIDATED:" + sql; // idempotent, like the real one
}

internal sealed class FakeDatasetRepo : IDatasetRepository
{
    public List<Dataset> Items { get; } = [];
    public Task AddAsync(Dataset dataset, CancellationToken ct = default) { Items.Add(dataset); return Task.CompletedTask; }
    public void Remove(Dataset dataset) => Items.Remove(dataset);
    public Task<Dataset?> GetAsync(Guid organizationId, Guid datasetId, CancellationToken ct = default) =>
        Task.FromResult(Items.SingleOrDefault(d => d.OrganizationId == organizationId && d.Id == datasetId));
    public Task<IReadOnlyList<Dataset>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Dataset>>(Items.Where(d => d.OrganizationId == organizationId).ToList());
    public Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeDatasetId, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(d => d.OrganizationId == organizationId
            && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase) && d.Id != excludeDatasetId));
}

internal sealed class FakeOrganizationRepo : IOrganizationRepository
{
    public List<Organization> Organizations { get; } = [];
    public List<OrganizationMember> Members { get; } = [];

    public Task AddAsync(Organization organization, OrganizationMember owner, CancellationToken ct = default)
    {
        Organizations.Add(organization);
        Members.Add(owner);
        return Task.CompletedTask;
    }

    public Task AddMemberAsync(OrganizationMember member, CancellationToken ct = default)
    {
        Members.Add(member);
        return Task.CompletedTask;
    }

    public Task<OrganizationMember?> GetMemberAsync(Guid organizationId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(Members.SingleOrDefault(m => m.OrganizationId == organizationId && m.UserId == userId));

    public Task<IReadOnlyList<OrganizationSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrganizationSummary>>(
            Members.Where(m => m.UserId == userId)
                .Select(m => new OrganizationSummary(m.OrganizationId, Organizations.Single(o => o.Id == m.OrganizationId).Name, m.Role))
                .ToList());
}

internal sealed class FakeUow : IUnitOfWork
{
    public int Saves { get; private set; }
    public Task<int> SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.FromResult(1); }
}

internal sealed class FakeSecrets : ISecretProtector
{
    public string Protect(string plainText) => "enc:" + plainText;
    public string Unprotect(string protectedText) => protectedText["enc:".Length..];
}

internal sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; } = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
}

internal sealed class FakeCurrentUser(Guid? userId) : ICurrentUser
{
    public Guid? UserId { get; set; } = userId;
}

internal sealed class FakeIdentity : IIdentityService
{
    public Dictionary<string, Guid> UsersByEmail { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult<Guid?>(UsersByEmail.TryGetValue(email, out var id) ? id : null);

    public Task<UserInfo> RegisterAsync(string email, string password, string displayName, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<AuthSession> LoginAsync(string email, string password, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<AuthSession> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task LogoutAsync(string refreshToken, CancellationToken ct = default) =>
        throw new NotSupportedException();
    public Task<UserInfo?> GetUserAsync(Guid userId, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

internal sealed class FakeRenderer : IChartSqlRenderer
{
    public Domain.DataSources.DataSourceType Type => Domain.DataSources.DataSourceType.PostgreSql;
    public string? LastDatasetSql { get; private set; }
    public ChartQueryPlan? LastPlan { get; private set; }
    public IReadOnlyList<object?>? LastDatasetParameters { get; private set; }

    public ParameterizedQuery Render(string datasetSql, ChartQueryPlan plan, IReadOnlyList<object?>? datasetParameters = null)
    {
        LastDatasetSql = datasetSql;
        LastPlan = plan;
        LastDatasetParameters = datasetParameters;
        return new ParameterizedQuery($"RENDERED({datasetSql})", datasetParameters ?? []);
    }
}

/// <summary>Writes a recognisable text for a plan, so tests can see what the renderer was given.</summary>
internal sealed class FakeTableViewRenderer : ITableViewSqlRenderer
{
    public DataSourceType Type => DataSourceType.PostgreSql;
    public TableViewPlan? LastPlan { get; private set; }

    public string Render(TableViewPlan plan)
    {
        LastPlan = plan;
        return "SELECT " + string.Join(", ", plan.Columns.Select(c => $"t{c.Table}.{c.Column} AS {c.Alias}"))
            + " FROM " + string.Join(" JOIN ", plan.Tables.Select(t => $"{t.Schema}.{t.Name}"));
    }
}
