using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

public class DataSourceExplorationTests
{
    private readonly FakeDataSourceRepo _repo = new();
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly FakeConnector _connector = new();
    private readonly FakeValidator _validator = new();
    private readonly Guid _org = Guid.NewGuid();
    private readonly DataSource _source;

    public DataSourceExplorationTests()
    {
        _source = DataSource.Create(_org, "Sales", DataSourceType.PostgreSql, new FakeSecrets().Protect("Host=db;Password=secret"), DateTime.UtcNow);
        _repo.Items.Add(_source);
    }

    private OrganizationAccess Access() => new(_orgs, _user);
    private DataSourceConnectors Connectors() => new([_connector]);

    private void Join(OrganizationRole role) =>
        _orgs.Members.Add(OrganizationMember.Create(_org, _user.UserId!.Value, role, DateTime.UtcNow));

    private PreviewQueryHandler Preview() => new(_repo, new FakeSecrets(), Connectors(), _validator, Access());
    private GetSchemaHandler Schema() => new(_repo, new FakeSecrets(), Connectors(), Access());
    private TestSavedConnectionHandler TestSaved() => new(_repo, new FakeSecrets(), Connectors(), Access());
    private TestNewConnectionHandler TestNew() => new(Connectors(), Access());

    [Fact]
    public async Task Stored_connection_string_is_decrypted_only_for_the_connector()
    {
        Join(OrganizationRole.Editor);

        await TestSaved().HandleAsync(_org, _source.Id);

        Assert.Equal(["Host=db;Password=secret"], _connector.UsedConnectionStrings);
    }

    [Fact]
    public async Task Viewer_can_browse_schema_but_cannot_test_preview_or_test_new()
    {
        Join(OrganizationRole.Viewer);

        await Schema().HandleAsync(_org, _source.Id);

        await Assert.ThrowsAsync<ForbiddenException>(() => TestSaved().HandleAsync(_org, _source.Id));
        await Assert.ThrowsAsync<ForbiddenException>(() => Preview().HandleAsync(_org, _source.Id, new PreviewQueryRequest("SELECT 1", null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => TestNew().HandleAsync(_org, new TestConnectionRequest(DataSourceType.PostgreSql, "Host=x")));
    }

    [Fact]
    public async Task Non_member_gets_not_found_everywhere()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Schema().HandleAsync(_org, _source.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Preview().HandleAsync(_org, _source.Id, new PreviewQueryRequest("SELECT 1", null)));
    }

    [Fact]
    public async Task Data_source_from_another_organization_is_not_reachable_through_my_organization()
    {
        // The user owns a different organization and guesses the id of a data source that belongs to _org.
        var mine = Guid.NewGuid();
        _orgs.Members.Add(OrganizationMember.Create(mine, _user.UserId!.Value, OrganizationRole.Owner, DateTime.UtcNow));

        await Assert.ThrowsAsync<NotFoundException>(() => Schema().HandleAsync(mine, _source.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Preview().HandleAsync(mine, _source.Id, new PreviewQueryRequest("SELECT 1", null)));
        Assert.Empty(_connector.UsedConnectionStrings);
    }

    [Fact]
    public async Task Unknown_data_source_id_is_not_found()
    {
        Join(OrganizationRole.Owner);

        await Assert.ThrowsAsync<NotFoundException>(() => Schema().HandleAsync(_org, Guid.NewGuid()));
    }

    [Fact]
    public async Task Preview_runs_the_validated_sql_not_the_raw_input()
    {
        Join(OrganizationRole.Editor);

        await Preview().HandleAsync(_org, _source.Id, new PreviewQueryRequest("SELECT 1", null));

        Assert.Equal("VALIDATED:SELECT 1", _connector.LastSql);
    }

    [Fact]
    public async Task Rejected_sql_never_reaches_the_connector()
    {
        Join(OrganizationRole.Editor);
        _validator.Reject = true;

        await Assert.ThrowsAsync<ValidationException>(() =>
            Preview().HandleAsync(_org, _source.Id, new PreviewQueryRequest("DROP TABLE x", null)));

        Assert.Empty(_connector.UsedConnectionStrings);
        Assert.Null(_connector.LastSql);
    }

    [Theory]
    [InlineData(null, PreviewQueryHandler.DefaultRows)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(25, 25)]
    [InlineData(100000, PreviewQueryHandler.MaxRows)]
    public async Task Row_limit_is_clamped(int? requested, int expected)
    {
        Join(OrganizationRole.Editor);

        await Preview().HandleAsync(_org, _source.Id, new PreviewQueryRequest("SELECT 1", requested));

        Assert.Equal(expected, _connector.LastMaxRows);
    }

    [Fact]
    public async Task Test_new_validates_the_connection_string_first()
    {
        Join(OrganizationRole.Editor);
        _connector.ValidationFailure = new ValidationException("ConnectionString", "bad");

        await Assert.ThrowsAsync<ValidationException>(() =>
            TestNew().HandleAsync(_org, new TestConnectionRequest(DataSourceType.PostgreSql, "Host=x;Passfile=/x")));

        Assert.Empty(_connector.UsedConnectionStrings);
    }

    [Fact]
    public async Task Unsupported_type_is_a_validation_error()
    {
        Join(OrganizationRole.Editor);

        await Assert.ThrowsAsync<ValidationException>(() =>
            TestNew().HandleAsync(_org, new TestConnectionRequest(DataSourceType.SqlServer, "Server=x")));
    }
}
