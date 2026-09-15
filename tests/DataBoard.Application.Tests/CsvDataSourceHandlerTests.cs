using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

internal sealed class FakeUploadStore : IUploadStore
{
    private int _next;
    public HashSet<string> Files { get; } = [];
    public List<string> Deleted { get; } = [];

    public string NewKey() => $"{++_next:x32}.db";
    public string PathOf(string key) => "/uploads/" + key;
    public bool Exists(string key) => Files.Contains(key);
    public void Delete(string key) { Files.Remove(key); Deleted.Add(key); }
}

internal sealed class FakeCsvImporter(FakeUploadStore store) : ICsvImporter
{
    public Exception? Failure { get; set; }
    public string TableName { get; set; } = "sales";
    public CsvImportOptions? LastOptions { get; private set; }
    public string? LastFileName { get; private set; }

    public Task<CsvImportResult> ImportAsync(Stream csv, string fileName, CsvImportOptions options, string destinationPath, CancellationToken ct = default)
    {
        LastOptions = options;
        LastFileName = fileName;
        if (Failure is not null) throw Failure;
        store.Files.Add(Path.GetFileName(destinationPath));
        return Task.FromResult(new CsvImportResult(options.TableName ?? TableName, 3, [new("a", "bigint")], ","));
    }
}

public class CsvDataSourceHandlerTests
{
    private readonly FakeDataSourceRepo _repo = new();
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeUow _uow = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly FakeUploadStore _store = new();
    private readonly FakeCsvImporter _importer;
    private readonly FakeConnector _connector = new() { Type = DataSourceType.Csv };
    private readonly Guid _org = Guid.NewGuid();

    public CsvDataSourceHandlerTests() => _importer = new FakeCsvImporter(_store);

    private OrganizationAccess Access() => new(_orgs, _user);
    private void Join(OrganizationRole role) => _orgs.Members.Add(OrganizationMember.Create(_org, _user.UserId!.Value, role, DateTime.UtcNow));

    private CreateCsvDataSourceHandler Create() =>
        new(_repo, _uow, new FakeSecrets(), new FixedClock(), Access(), _importer, _store);

    private ReplaceCsvDataHandler Replace() =>
        new(_repo, _uow, new FakeSecrets(), Access(), _importer, _store, new DataSourceConnectors([_connector]));

    private DeleteDataSourceHandler Delete() => new(_repo, _uow, new FakeSecrets(), Access(), _store);

    private Task<CsvUploadResult> Upload(string name = "Sales", string fileName = "sales.csv", CsvImportOptions? options = null) =>
        Create().HandleAsync(_org, name, fileName, new MemoryStream([1]), options ?? new CsvImportOptions());

    // ---- create ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_editor_uploads_a_file_and_gets_a_data_source_that_points_at_it()
    {
        Join(OrganizationRole.Editor);

        var result = await Upload();

        var saved = Assert.Single(_repo.Items);
        Assert.Equal(DataSourceType.Csv, saved.Type);
        Assert.Equal("Sales", result.DataSource.Name);
        Assert.Equal("sales", result.TableName);
        Assert.Equal(3, result.RowCount);
        var key = Assert.Single(_store.Files);
        Assert.Equal("enc:" + CsvConnection.For(key), saved.EncryptedConnectionString); // stored encrypted, like any connection
        Assert.Equal(1, _uow.Saves);
    }

    [Fact]
    public async Task The_file_name_and_options_go_to_the_importer_as_they_were_given()
    {
        Join(OrganizationRole.Editor);

        await Upload(fileName: "q1.csv", options: new CsvImportOptions("mine", false, ";"));

        Assert.Equal("q1.csv", _importer.LastFileName);
        Assert.Equal(new CsvImportOptions("mine", false, ";"), _importer.LastOptions);
    }

    [Fact]
    public async Task A_viewer_is_forbidden_and_no_file_is_written()
    {
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<ForbiddenException>(() => Upload());

        Assert.Empty(_repo.Items);
        Assert.Empty(_store.Files);
    }

    [Fact]
    public async Task A_non_member_gets_not_found()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Upload());
        Assert.Empty(_store.Files);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_name_is_required_and_the_file_is_not_even_read(string name)
    {
        Join(OrganizationRole.Editor);

        var error = await Assert.ThrowsAsync<ValidationException>(() => Upload(name));

        Assert.Contains("Name", error.Errors.Keys);
        Assert.Null(_importer.LastFileName);
    }

    [Fact]
    public async Task A_name_that_is_too_long_is_refused()
    {
        Join(OrganizationRole.Editor);

        var error = await Assert.ThrowsAsync<ValidationException>(() => Upload(new string('x', DataSource.MaxNameLength + 1)));

        Assert.Contains("Name", error.Errors.Keys);
    }

    [Fact]
    public async Task A_file_that_cannot_be_imported_leaves_nothing_behind()
    {
        Join(OrganizationRole.Editor);
        _importer.Failure = new ValidationException("File", "The file is empty.");

        await Assert.ThrowsAsync<ValidationException>(() => Upload());

        Assert.Empty(_repo.Items);
        Assert.Equal(0, _uow.Saves);
        Assert.Empty(_store.Files);
    }

    // ---- replace --------------------------------------------------------------------------------------------

    private async Task<Guid> ExistingUpload()
    {
        Join(OrganizationRole.Editor);
        _importer.TableName = "orders";
        return (await Upload()).DataSource.Id;
    }

    [Fact]
    public async Task Replacing_the_file_keeps_the_table_name_and_removes_the_old_file()
    {
        var id = await ExistingUpload();
        var oldFile = Assert.Single(_store.Files);
        _connector.Schema = new DatabaseSchema([new("main", [new("orders", "Table", [])])], false);
        _importer.TableName = "ignored";

        var result = await Replace().HandleAsync(_org, id, "new.csv", new MemoryStream([1]), new CsvImportOptions());

        Assert.Equal("orders", _importer.LastOptions!.TableName); // what the datasets are written against
        Assert.Equal("orders", result.TableName);
        var newFile = Assert.Single(_store.Files);
        Assert.NotEqual(oldFile, newFile);
        Assert.Equal([oldFile], _store.Deleted);
        Assert.Equal("enc:" + CsvConnection.For(newFile), _repo.Items.Single().EncryptedConnectionString);
        Assert.Equal(2, _uow.Saves);
    }

    [Fact]
    public async Task A_replacement_that_fails_keeps_the_old_file_and_the_old_connection()
    {
        var id = await ExistingUpload();
        var oldFile = Assert.Single(_store.Files);
        _connector.Schema = new DatabaseSchema([new("main", [new("orders", "Table", [])])], false);
        _importer.Failure = new ValidationException("File", "bad");

        await Assert.ThrowsAsync<ValidationException>(() =>
            Replace().HandleAsync(_org, id, "new.csv", new MemoryStream([1]), new CsvImportOptions()));

        Assert.Equal([oldFile], _store.Files);
        Assert.Equal("enc:" + CsvConnection.For(oldFile), _repo.Items.Single().EncryptedConnectionString);
        Assert.Equal(1, _uow.Saves);
    }

    [Fact]
    public async Task Only_a_csv_data_source_can_have_its_file_replaced()
    {
        Join(OrganizationRole.Editor);
        var other = DataSource.Create(_org, "Pg", DataSourceType.PostgreSql, "enc:Host=db", DateTime.UtcNow);
        _repo.Items.Add(other);

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            Replace().HandleAsync(_org, other.Id, "x.csv", new MemoryStream([1]), new CsvImportOptions()));

        Assert.Contains("Type", error.Errors.Keys);
        Assert.Empty(_store.Files);
    }

    [Fact]
    public async Task Replacing_needs_an_editor_and_a_data_source_of_this_organization()
    {
        var id = await ExistingUpload();
        _orgs.Members.Clear();
        Join(OrganizationRole.Viewer);
        await Assert.ThrowsAsync<ForbiddenException>(() => Replace().HandleAsync(_org, id, "x.csv", new MemoryStream([1]), new CsvImportOptions()));

        _orgs.Members.Clear();
        Join(OrganizationRole.Editor);
        await Assert.ThrowsAsync<NotFoundException>(() => Replace().HandleAsync(_org, Guid.NewGuid(), "x.csv", new MemoryStream([1]), new CsvImportOptions()));
    }

    // ---- delete ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_owner_deletes_a_csv_data_source_and_its_file()
    {
        var id = await ExistingUpload();
        var file = Assert.Single(_store.Files);
        _orgs.Members.Clear();
        Join(OrganizationRole.Owner);

        await Delete().HandleAsync(_org, id);

        Assert.Empty(_repo.Items);
        Assert.Empty(_store.Files);
        Assert.Equal([file], _store.Deleted);
    }

    [Fact]
    public async Task Deleting_a_database_data_source_touches_no_file()
    {
        Join(OrganizationRole.Owner);
        var pg = DataSource.Create(_org, "Pg", DataSourceType.PostgreSql, "enc:Data Source=0123", DateTime.UtcNow);
        _repo.Items.Add(pg);

        await Delete().HandleAsync(_org, pg.Id);

        Assert.Empty(_repo.Items);
        Assert.Empty(_store.Deleted);
    }

    [Theory]
    [InlineData(OrganizationRole.Editor)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task Only_an_owner_can_delete(OrganizationRole role)
    {
        var id = await ExistingUpload();
        _orgs.Members.Clear();
        Join(role);

        await Assert.ThrowsAsync<ForbiddenException>(() => Delete().HandleAsync(_org, id));

        Assert.Single(_repo.Items);
        Assert.Single(_store.Files);
    }

    [Fact]
    public async Task Deleting_something_that_is_not_there_is_not_found()
    {
        Join(OrganizationRole.Owner);

        await Assert.ThrowsAsync<NotFoundException>(() => Delete().HandleAsync(_org, Guid.NewGuid()));
    }

    // ---- the connection of an upload ------------------------------------------------------------------------

    [Theory]
    [InlineData("Data Source=abc.db", true, "abc.db")]
    [InlineData("Data Source=", false, "")]
    [InlineData("Host=db", false, "")]
    [InlineData("data source=abc.db", false, "")]
    public void The_key_is_read_from_a_connection_only_when_it_has_the_exact_form(string connection, bool ok, string expected)
    {
        Assert.Equal(ok, CsvConnection.TryGetKey(connection, out var key));
        Assert.Equal(expected, key);
    }
}
