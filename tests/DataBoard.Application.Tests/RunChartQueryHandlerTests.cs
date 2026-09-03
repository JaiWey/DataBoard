using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

public class RunChartQueryHandlerTests
{
    private readonly FakeDatasetRepo _datasets = new();
    private readonly FakeDataSourceRepo _sources = new();
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly FakeConnector _connector = new();
    private readonly FakeValidator _validator = new();
    private readonly FakeRenderer _renderer = new();
    private readonly Guid _org = Guid.NewGuid();
    private readonly Dataset _dataset;

    private static readonly ChartQuery Count = new(Measures: [new(Aggregation.Count)]);

    public RunChartQueryHandlerTests()
    {
        var source = DataSource.Create(_org, "DB", DataSourceType.PostgreSql, new FakeSecrets().Protect("Host=db;Password=secret"), DateTime.UtcNow);
        _sources.Items.Add(source);
        _dataset = Dataset.Create(_org, source.Id, "Orders", null, "SELECT region, amount FROM sales",
            [new("region", "text", ColumnRole.Dimension), new("amount", "numeric", ColumnRole.Measure)], Guid.NewGuid(), DateTime.UtcNow);
        _datasets.Items.Add(_dataset);
    }

    private RunChartQueryHandler Handler() => new(
        _datasets, _sources, new FakeSecrets(), new DataSourceConnectors([_connector]),
        new ChartSqlRenderers([_renderer]), _validator, new OrganizationAccess(_orgs, _user));

    private void Join(OrganizationRole role, Guid? org = null) =>
        _orgs.Members.Add(OrganizationMember.Create(org ?? _org, _user.UserId!.Value, role, DateTime.UtcNow));

    [Fact]
    public async Task Viewers_can_run_queries_but_do_not_get_the_generated_sql()
    {
        Join(OrganizationRole.Viewer);

        var result = await Handler().HandleAsync(_org, _dataset.Id, Count);

        Assert.Null(result.Sql);
        Assert.Equal(3L, result.Rows.Single()[0]);
    }

    [Fact]
    public async Task Editors_get_the_generated_sql_for_debugging()
    {
        Join(OrganizationRole.Editor);

        var result = await Handler().HandleAsync(_org, _dataset.Id, Count);

        Assert.Equal("RENDERED(VALIDATED:SELECT region, amount FROM sales)", result.Sql);
    }

    [Fact]
    public async Task The_renderer_gets_the_revalidated_dataset_sql_and_the_connector_gets_the_decrypted_secret()
    {
        Join(OrganizationRole.Viewer);

        await Handler().HandleAsync(_org, _dataset.Id, new ChartQuery(
            Dimensions: [new("region")], Measures: [new(Aggregation.Sum, "amount")], Limit: 25));

        Assert.Equal("VALIDATED:SELECT region, amount FROM sales", _renderer.LastDatasetSql);
        Assert.Equal(25, _connector.LastExecuteMaxRows);
        Assert.Equal(["Host=db;Password=secret"], _connector.UsedConnectionStrings);
    }

    [Fact]
    public async Task Stored_sql_that_no_longer_validates_is_never_rendered_or_run()
    {
        Join(OrganizationRole.Viewer);
        _validator.Reject = true;

        await Assert.ThrowsAsync<ValidationException>(() => Handler().HandleAsync(_org, _dataset.Id, Count));

        Assert.Null(_renderer.LastPlan);
        Assert.Null(_connector.LastQuery);
    }

    [Fact]
    public async Task An_invalid_chart_query_stops_before_any_sql_is_built()
    {
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Handler().HandleAsync(_org, _dataset.Id, new ChartQuery(Dimensions: [new("not_a_column")])));

        Assert.Null(_renderer.LastPlan);
        Assert.Null(_connector.LastQuery);
    }

    [Fact]
    public async Task A_missing_body_is_treated_as_an_empty_query_and_rejected()
    {
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<ValidationException>(() => Handler().HandleAsync(_org, _dataset.Id, null!));
    }

    [Fact]
    public async Task Non_members_get_not_found_and_nothing_runs()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Handler().HandleAsync(_org, _dataset.Id, Count));

        Assert.Null(_connector.LastQuery);
    }

    [Fact]
    public async Task A_dataset_from_another_organization_is_not_reachable()
    {
        var mine = Guid.NewGuid();
        Join(OrganizationRole.Owner, mine);

        await Assert.ThrowsAsync<NotFoundException>(() => Handler().HandleAsync(mine, _dataset.Id, Count));

        Assert.Null(_connector.LastQuery);
    }

    [Fact]
    public async Task Unknown_dataset_ids_are_not_found()
    {
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<NotFoundException>(() => Handler().HandleAsync(_org, Guid.NewGuid(), Count));
    }

    [Fact]
    public async Task Anonymous_callers_are_unauthorized()
    {
        _user.UserId = null;

        await Assert.ThrowsAsync<UnauthorizedException>(() => Handler().HandleAsync(_org, _dataset.Id, Count));
    }
}
