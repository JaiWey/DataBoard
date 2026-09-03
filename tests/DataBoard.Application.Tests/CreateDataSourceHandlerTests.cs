using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

public class CreateDataSourceHandlerTests
{
    private readonly FakeDataSourceRepo _repo = new();
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeUow _uow = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly FakeConnector _connector = new();
    private readonly Guid _org = Guid.NewGuid();

    private CreateDataSourceHandler Create() =>
        new(_repo, _uow, new FakeSecrets(), new FixedClock(), new OrganizationAccess(_orgs, _user),
            new DataSourceConnectors([_connector]));

    private void Join(OrganizationRole role) =>
        _orgs.Members.Add(OrganizationMember.Create(_org, _user.UserId!.Value, role, DateTime.UtcNow));

    private CreateDataSourceRequest Request(string name = "Sales", string cs = "Host=db") =>
        new(_org, name, DataSourceType.PostgreSql, cs);

    [Fact]
    public async Task Editor_can_create_and_connection_string_is_stored_encrypted()
    {
        Join(OrganizationRole.Editor);

        var dto = await Create().HandleAsync(Request());

        Assert.Equal("enc:Host=db", Assert.Single(_repo.Items).EncryptedConnectionString);
        Assert.Equal(1, _uow.Saves);
        Assert.Equal("Sales", dto.Name);
    }

    [Fact]
    public async Task Viewer_is_forbidden_and_nothing_is_saved()
    {
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<ForbiddenException>(() => Create().HandleAsync(Request()));

        Assert.Empty(_repo.Items);
        Assert.Equal(0, _uow.Saves);
    }

    [Fact]
    public async Task Non_member_gets_not_found_so_organizations_cannot_be_probed()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Create().HandleAsync(Request()));
    }

    [Fact]
    public async Task Anonymous_caller_is_unauthorized()
    {
        _user.UserId = null;

        await Assert.ThrowsAsync<UnauthorizedException>(() => Create().HandleAsync(Request()));
    }

    [Fact]
    public async Task Blank_connection_string_is_a_validation_error()
    {
        Join(OrganizationRole.Owner);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(Request(cs: " ")));

        Assert.Contains("ConnectionString", ex.Errors.Keys);
        Assert.Empty(_repo.Items);
    }

    [Fact]
    public async Task Blank_name_is_a_validation_error()
    {
        Join(OrganizationRole.Owner);

        await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(Request(name: " ")));
    }

    [Fact]
    public async Task Connection_string_is_checked_by_the_connector_before_anything_is_saved()
    {
        Join(OrganizationRole.Owner);
        _connector.ValidationFailure = new ValidationException("ConnectionString", "option not allowed");

        await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(Request(cs: "Host=x;Passfile=/etc/passwd")));

        Assert.Equal(["Host=x;Passfile=/etc/passwd"], _connector.ValidatedConnectionStrings);
        Assert.Empty(_repo.Items);
        Assert.Equal(0, _uow.Saves);
    }

    [Fact]
    public async Task Types_without_a_connector_are_rejected()
    {
        Join(OrganizationRole.Owner);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Create().HandleAsync(new CreateDataSourceRequest(_org, "Mssql", DataSourceType.SqlServer, "Server=x")));

        Assert.Contains("Type", ex.Errors.Keys);
        Assert.Empty(_repo.Items);
    }

    [Fact]
    public async Task Viewer_can_list_but_only_their_own_organizations_data_sources()
    {
        Join(OrganizationRole.Viewer);
        var otherOrg = Guid.NewGuid();
        _repo.Items.Add(DataSource.Create(_org, "Mine", DataSourceType.Csv, "x", DateTime.UtcNow));
        _repo.Items.Add(DataSource.Create(otherOrg, "Theirs", DataSourceType.Csv, "x", DateTime.UtcNow));
        var list = new ListDataSourcesHandler(_repo, new OrganizationAccess(_orgs, _user));

        var mine = await list.HandleAsync(_org);
        Assert.Equal("Mine", Assert.Single(mine).Name);

        await Assert.ThrowsAsync<NotFoundException>(() => list.HandleAsync(otherOrg));
    }
}
