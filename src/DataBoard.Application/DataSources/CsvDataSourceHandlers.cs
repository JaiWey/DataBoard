using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.DataSources;

/// <summary>What the person is told about a file that was imported.</summary>
public sealed record CsvUploadResult(DataSourceDto DataSource, string TableName, int RowCount, IReadOnlyList<CsvColumn> Columns);

/// <summary>
/// The "connection string" of an uploaded file is only the key of its database file. It is stored encrypted like any other
/// connection string, and it is never accepted from a user: it comes from the upload store.
/// </summary>
public static class CsvConnection
{
    private const string Prefix = "Data Source=";

    public static string For(string key) => Prefix + key;

    public static bool TryGetKey(string connectionString, out string key)
    {
        key = connectionString.StartsWith(Prefix, StringComparison.Ordinal) ? connectionString[Prefix.Length..] : string.Empty;
        return key.Length > 0;
    }
}

/// <summary>
/// Makes a data source from an uploaded CSV file. The file is read into a small database of its own and nothing of the
/// original is kept, so what can be queried is only what was imported, and only for reading.
/// </summary>
public sealed class CreateCsvDataSourceHandler(
    IDataSourceRepository repository,
    IUnitOfWork unitOfWork,
    ISecretProtector secrets,
    IClock clock,
    OrganizationAccess access,
    ICsvImporter importer,
    IUploadStore store)
{
    public async Task<CsvUploadResult> HandleAsync(
        Guid organizationId, string name, string fileName, Stream file, CsvImportOptions options, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        if (string.IsNullOrWhiteSpace(name))
            throw new ValidationException("Name", "Name is required.");
        if (name.Trim().Length > DataSource.MaxNameLength)
            throw new ValidationException("Name", $"Name must be at most {DataSource.MaxNameLength} characters.");

        var key = store.NewKey();
        try
        {
            var imported = await importer.ImportAsync(file, fileName, options, store.PathOf(key), ct);

            var dataSource = DatasetQuery.Guard(() => DataSource.Create(
                organizationId, name, DataSourceType.Csv, secrets.Protect(CsvConnection.For(key)), clock.UtcNow));
            await repository.AddAsync(dataSource, ct);
            await unitOfWork.SaveChangesAsync(ct);

            return new CsvUploadResult(dataSource.ToDto(), imported.TableName, imported.RowCount, imported.Columns);
        }
        catch
        {
            store.Delete(key); // a file that did not become a data source must not stay behind
            throw;
        }
    }
}

/// <summary>
/// Puts new data into an existing CSV data source: the file is imported again under the same table name, so the datasets
/// and charts built on it carry on, and the old file is removed once the new one is in place. If the new file has
/// different columns, what used them says so when it runs.
/// </summary>
public sealed class ReplaceCsvDataHandler(
    IDataSourceRepository repository,
    IUnitOfWork unitOfWork,
    ISecretProtector secrets,
    OrganizationAccess access,
    ICsvImporter importer,
    IUploadStore store,
    DataSourceConnectors connectors)
{
    public async Task<CsvUploadResult> HandleAsync(
        Guid organizationId, Guid dataSourceId, string fileName, Stream file, CsvImportOptions options, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dataSource = await repository.GetForUpdateAsync(organizationId, dataSourceId, ct)
            ?? throw new NotFoundException("Data source not found.");
        if (dataSource.Type != DataSourceType.Csv)
            throw new ValidationException("Type", "Only a CSV data source can have its file replaced.");

        var current = secrets.Unprotect(dataSource.EncryptedConnectionString);
        var oldKey = CsvConnection.TryGetKey(current, out var found) ? found : null;

        // Keep the table's name: the datasets written against it refer to it.
        var schema = await connectors.Get(DataSourceType.Csv).GetSchemaAsync(current, ct);
        var tableName = schema.Schemas.SelectMany(s => s.Tables).FirstOrDefault()?.Name;

        var newKey = store.NewKey();
        try
        {
            var imported = await importer.ImportAsync(
                file, fileName, options with { TableName = tableName ?? options.TableName }, store.PathOf(newKey), ct);

            dataSource.ReplaceConnection(secrets.Protect(CsvConnection.For(newKey)));
            await unitOfWork.SaveChangesAsync(ct);

            if (oldKey is not null) store.Delete(oldKey);
            return new CsvUploadResult(dataSource.ToDto(), imported.TableName, imported.RowCount, imported.Columns);
        }
        catch
        {
            store.Delete(newKey);
            throw;
        }
    }
}

/// <summary>
/// Removes a data source, with the datasets and charts built on it, and for a CSV its file. This is heavier than deleting
/// one chart, so it is for Owners.
/// </summary>
public sealed class DeleteDataSourceHandler(
    IDataSourceRepository repository, IUnitOfWork unitOfWork, ISecretProtector secrets, OrganizationAccess access, IUploadStore store)
{
    public async Task HandleAsync(Guid organizationId, Guid dataSourceId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Owner, ct);

        var dataSource = await repository.GetForUpdateAsync(organizationId, dataSourceId, ct)
            ?? throw new NotFoundException("Data source not found.");
        var file = dataSource.Type == DataSourceType.Csv && CsvConnection.TryGetKey(secrets.Unprotect(dataSource.EncryptedConnectionString), out var key)
            ? key
            : null;

        repository.Remove(dataSource);
        await unitOfWork.SaveChangesAsync(ct);

        if (file is not null) store.Delete(file);
    }
}
