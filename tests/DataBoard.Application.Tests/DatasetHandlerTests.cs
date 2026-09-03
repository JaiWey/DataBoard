using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

public class DatasetHandlerTests
{
    private readonly FakeDatasetRepo _datasets = new();
    private readonly FakeDataSourceRepo _sources = new();
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeUow _uow = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly FakeConnector _connector = new();
    private readonly FakeValidator _validator = new();
    private readonly Guid _org = Guid.NewGuid();
    private readonly DataSource _source;

    public DatasetHandlerTests()
    {
        _source = DataSource.Create(_org, "Sales DB", DataSourceType.PostgreSql, new FakeSecrets().Protect("Host=db;Password=secret"), DateTime.UtcNow);
        _sources.Items.Add(_source);
    }

    private OrganizationAccess Access() => new(_orgs, _user);
    private DataSourceConnectors Connectors() => new([_connector]);

    private void Join(OrganizationRole role, Guid? org = null) =>
        _orgs.Members.Add(OrganizationMember.Create(org ?? _org, _user.UserId!.Value, role, DateTime.UtcNow));

    private CreateDatasetHandler Create() =>
        new(_datasets, _sources, new FakeSecrets(), Connectors(), _validator, _uow, new FixedClock(), Access());

    private UpdateDatasetHandler Update() =>
        new(_datasets, _sources, new FakeSecrets(), Connectors(), _validator, _uow, new FixedClock(), Access());

    private PreviewDatasetHandler Preview() =>
        new(_datasets, _sources, new FakeSecrets(), Connectors(), _validator, Access());

    private CreateDatasetRequest Request(string name = "Orders", string sql = "SELECT id, name FROM t") =>
        new(_source.Id, name, "All orders", sql);

    // ---- create --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Editor_creates_a_dataset_with_validated_sql_and_inferred_column_roles()
    {
        Join(OrganizationRole.Editor);
        _connector.DescribedColumns =
        [
            new("region", "text"), new("amount", "numeric(10,2)"), new("qty", "integer"),
            new("sold_at", "timestamp with time zone"), new("tags", "integer[]"),
        ];

        var dto = await Create().HandleAsync(_org, Request());

        var stored = Assert.Single(_datasets.Items);
        Assert.Equal("VALIDATED:SELECT id, name FROM t", stored.Sql);
        Assert.Equal(1, _uow.Saves);
        Assert.Equal(
            [ColumnRole.Dimension, ColumnRole.Measure, ColumnRole.Measure, ColumnRole.Dimension, ColumnRole.Dimension],
            dto.Columns.Select(c => c.Role));
        Assert.Equal("VALIDATED:SELECT id, name FROM t", dto.Sql);
        Assert.Equal(_user.UserId, stored.CreatedByUserId);
    }

    [Fact]
    public async Task The_stored_connection_string_is_decrypted_only_to_describe_the_query()
    {
        Join(OrganizationRole.Editor);

        await Create().HandleAsync(_org, Request());

        Assert.Equal(["Host=db;Password=secret"], _connector.UsedConnectionStrings);
        Assert.Equal(1, _connector.DescribeCalls);
    }

    [Fact]
    public async Task Viewer_cannot_create_and_non_member_gets_not_found()
    {
        Join(OrganizationRole.Viewer);
        await Assert.ThrowsAsync<ForbiddenException>(() => Create().HandleAsync(_org, Request()));

        _user.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => Create().HandleAsync(_org, Request()));

        Assert.Empty(_datasets.Items);
        Assert.Equal(0, _connector.DescribeCalls);
    }

    [Fact]
    public async Task A_data_source_from_another_organization_cannot_be_used()
    {
        var mine = Guid.NewGuid();
        Join(OrganizationRole.Owner, mine);

        await Assert.ThrowsAsync<NotFoundException>(() => Create().HandleAsync(mine, Request()));

        Assert.Empty(_datasets.Items);
    }

    [Fact]
    public async Task Rejected_sql_is_never_described_or_saved()
    {
        Join(OrganizationRole.Editor);
        _validator.Reject = true;

        await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(_org, Request(sql: "DROP TABLE t")));

        Assert.Equal(0, _connector.DescribeCalls);
        Assert.Empty(_datasets.Items);
        Assert.Equal(0, _uow.Saves);
    }

    [Fact]
    public async Task Duplicate_names_conflict_regardless_of_case()
    {
        Join(OrganizationRole.Editor);
        await Create().HandleAsync(_org, Request("Orders"));

        await Assert.ThrowsAsync<ConflictException>(() => Create().HandleAsync(_org, Request("ORDERS")));

        Assert.Single(_datasets.Items);
    }

    [Fact]
    public async Task Duplicate_column_names_are_a_validation_error_that_tells_the_author_to_alias()
    {
        Join(OrganizationRole.Editor);
        _connector.DescribedColumns = [new("total", "numeric"), new("Total", "numeric")];

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(_org, Request()));

        Assert.Contains("alias", ex.Errors.Values.Single().Single());
        Assert.Empty(_datasets.Items);
    }

    [Fact]
    public async Task Blank_name_and_empty_result_shape_are_validation_errors()
    {
        Join(OrganizationRole.Editor);
        await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(_org, Request(name: " ")));

        _connector.DescribedColumns = [];
        await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(_org, Request()));
    }

    // ---- read ----------------------------------------------------------------------------------------------

    private async Task<DatasetDto> SeedAsync()
    {
        Join(OrganizationRole.Owner);
        var dto = await Create().HandleAsync(_org, Request());
        _orgs.Members.Clear();
        return dto;
    }

    [Fact]
    public async Task Viewers_see_datasets_without_the_sql_and_editors_see_it()
    {
        var created = await SeedAsync();

        Join(OrganizationRole.Viewer);
        var asViewer = await new GetDatasetHandler(_datasets, Access()).HandleAsync(_org, created.Id);
        var listedAsViewer = await new ListDatasetsHandler(_datasets, Access()).HandleAsync(_org);
        Assert.Null(asViewer.Sql);
        Assert.Null(Assert.Single(listedAsViewer).Sql);
        Assert.NotEmpty(asViewer.Columns);

        _orgs.Members.Clear();
        Join(OrganizationRole.Editor);
        Assert.NotNull((await new GetDatasetHandler(_datasets, Access()).HandleAsync(_org, created.Id)).Sql);
        Assert.NotNull(Assert.Single(await new ListDatasetsHandler(_datasets, Access()).HandleAsync(_org)).Sql);
    }

    [Fact]
    public async Task Datasets_are_not_visible_through_another_organization()
    {
        var created = await SeedAsync();
        var mine = Guid.NewGuid();
        Join(OrganizationRole.Owner, mine);

        await Assert.ThrowsAsync<NotFoundException>(() => new GetDatasetHandler(_datasets, Access()).HandleAsync(mine, created.Id));
        Assert.Empty(await new ListDatasetsHandler(_datasets, Access()).HandleAsync(mine));
    }

    // ---- update --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Update_re_validates_and_re_describes_the_query()
    {
        var created = await SeedAsync();
        Join(OrganizationRole.Editor);
        _connector.DescribedColumns = [new("only_col", "bigint")];

        var updated = await Update().HandleAsync(_org, created.Id, new UpdateDatasetRequest("Orders v2", null, "SELECT 1 AS only_col"));

        Assert.Equal("Orders v2", updated.Name);
        Assert.Equal("VALIDATED:SELECT 1 AS only_col", updated.Sql);
        var column = Assert.Single(updated.Columns);
        Assert.Equal(("only_col", ColumnRole.Measure), (column.Name, column.Role));
    }

    [Fact]
    public async Task Update_can_keep_its_own_name_but_not_take_another_datasets_name()
    {
        var first = await SeedAsync();
        Join(OrganizationRole.Editor);
        await Create().HandleAsync(_org, Request("Second"));

        await Update().HandleAsync(_org, first.Id, new UpdateDatasetRequest("orders", null, "SELECT 1"));
        await Assert.ThrowsAsync<ConflictException>(() =>
            Update().HandleAsync(_org, first.Id, new UpdateDatasetRequest("Second", null, "SELECT 1")));
    }

    [Fact]
    public async Task Update_with_rejected_sql_leaves_the_dataset_unchanged()
    {
        var created = await SeedAsync();
        Join(OrganizationRole.Editor);
        _validator.Reject = true;

        await Assert.ThrowsAsync<ValidationException>(() =>
            Update().HandleAsync(_org, created.Id, new UpdateDatasetRequest("Hacked", null, "DROP TABLE t")));

        Assert.Equal("Orders", Assert.Single(_datasets.Items).Name);
    }

    [Fact]
    public async Task Viewers_cannot_update_or_delete()
    {
        var created = await SeedAsync();
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            Update().HandleAsync(_org, created.Id, new UpdateDatasetRequest("X", null, "SELECT 1")));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            new DeleteDatasetHandler(_datasets, _uow, Access()).HandleAsync(_org, created.Id));
        Assert.Single(_datasets.Items);
    }

    // ---- delete --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Editor_can_delete_and_unknown_ids_are_not_found()
    {
        var created = await SeedAsync();
        Join(OrganizationRole.Editor);
        var delete = new DeleteDatasetHandler(_datasets, _uow, Access());

        await Assert.ThrowsAsync<NotFoundException>(() => delete.HandleAsync(_org, Guid.NewGuid()));
        await delete.HandleAsync(_org, created.Id);

        Assert.Empty(_datasets.Items);
    }

    // ---- preview -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Viewers_can_preview_data_and_the_row_limit_is_clamped()
    {
        var created = await SeedAsync();
        Join(OrganizationRole.Viewer);

        await Preview().HandleAsync(_org, created.Id, 100_000);

        Assert.Equal(PreviewQueryHandler.MaxRows, _connector.LastMaxRows);
        Assert.Equal("VALIDATED:SELECT id, name FROM t", _connector.LastSql);
    }

    [Fact]
    public async Task Stored_sql_is_validated_again_before_every_run()
    {
        var created = await SeedAsync();
        Join(OrganizationRole.Viewer);
        _validator.Reject = true; // as if the stored text had been altered outside the application

        await Assert.ThrowsAsync<ValidationException>(() => Preview().HandleAsync(_org, created.Id, null));

        Assert.Equal(0, _connector.PreviewCalls);
    }

    [Fact]
    public async Task Preview_of_a_dataset_in_another_organization_is_not_found()
    {
        var created = await SeedAsync();
        var mine = Guid.NewGuid();
        Join(OrganizationRole.Owner, mine);

        await Assert.ThrowsAsync<NotFoundException>(() => Preview().HandleAsync(mine, created.Id, null));
        Assert.Equal(0, _connector.PreviewCalls);
    }
}

public class ColumnRoleInferenceTests
{
    [Theory]
    [InlineData("integer", ColumnRole.Measure)]
    [InlineData("bigint", ColumnRole.Measure)]
    [InlineData("smallint", ColumnRole.Measure)]
    [InlineData("numeric", ColumnRole.Measure)]
    [InlineData("numeric(10,2)", ColumnRole.Measure)]
    [InlineData("double precision", ColumnRole.Measure)]
    [InlineData("real", ColumnRole.Measure)]
    [InlineData("INTEGER", ColumnRole.Measure)]
    [InlineData("text", ColumnRole.Dimension)]
    [InlineData("character varying(256)", ColumnRole.Dimension)]
    [InlineData("boolean", ColumnRole.Dimension)]
    [InlineData("uuid", ColumnRole.Dimension)]
    [InlineData("date", ColumnRole.Dimension)]
    [InlineData("timestamp with time zone", ColumnRole.Dimension)]
    [InlineData("integer[]", ColumnRole.Dimension)]
    [InlineData("jsonb", ColumnRole.Dimension)]
    [InlineData("money", ColumnRole.Dimension)]
    public void Numbers_are_measures_and_everything_else_is_a_dimension(string dataType, ColumnRole expected) =>
        Assert.Equal(expected, ColumnRoleInference.Infer(dataType));
}
