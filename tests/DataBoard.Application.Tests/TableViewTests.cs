using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

public class TableViewPlannerTests
{
    private static ColumnSchema C(string name) => new(name, "text", true);

    private static readonly DatabaseSchema Schema = new(
        [
            new SchemaNode("public",
            [
                new TableSchema("orders", "Table", [C("id"), C("customer_id"), C("amount")]),
                new TableSchema("customers", "Table", [C("id"), C("name")]),
                new TableSchema("regions", "View", [C("id"), C("label")]),
            ]),
        ],
        false);

    private static TableViewTable T(string name, params string[] fields) => new("public", name, fields);

    private static string Fails(TableViewDefinition? definition) =>
        string.Join(" ", Assert.Throws<ValidationException>(() => TableViewPlanner.Plan(Schema, definition)).Errors.Values.SelectMany(v => v));

    [Fact]
    public void A_single_table_view_selects_the_chosen_fields()
    {
        var plan = TableViewPlanner.Plan(Schema, new TableViewDefinition(T("orders", "id", "amount")));

        Assert.Single(plan.Tables);
        Assert.Equal(["id", "amount"], plan.Columns.Select(c => c.Alias));
        Assert.All(plan.Columns, c => Assert.Equal(0, c.Table));
    }

    [Fact]
    public void A_join_links_a_field_of_an_earlier_step_to_a_field_of_the_new_table()
    {
        var definition = new TableViewDefinition(
            T("orders", "id", "amount"),
            [new TableViewJoin(JoinType.Left, T("customers", "name"), [new JoinCondition(0, "customer_id", "id")])]);

        var plan = TableViewPlanner.Plan(Schema, definition);

        Assert.Equal(2, plan.Tables.Count);
        Assert.Equal(JoinType.Left, plan.Tables[1].Join);
        Assert.Equal(new PlannedCondition(0, "customer_id", "id"), Assert.Single(plan.Tables[1].On));
        Assert.Equal(["id", "amount", "name"], plan.Columns.Select(c => c.Alias));
    }

    [Fact]
    public void Fields_with_the_same_name_from_different_tables_get_unique_names()
    {
        var definition = new TableViewDefinition(
            T("orders", "id"),
            [new TableViewJoin(JoinType.Inner, T("customers", "id", "name"), [new JoinCondition(0, "customer_id", "id")])]);

        var plan = TableViewPlanner.Plan(Schema, definition);

        Assert.Equal(["id", "customers_id", "name"], plan.Columns.Select(c => c.Alias));
    }

    [Fact]
    public void A_table_can_be_joined_to_itself_and_its_fields_stay_unique()
    {
        var definition = new TableViewDefinition(
            T("customers", "id"),
            [new TableViewJoin(JoinType.Inner, T("customers", "id"), [new JoinCondition(0, "id", "id")])]);

        var plan = TableViewPlanner.Plan(Schema, definition);

        Assert.Equal(["id", "customers_id"], plan.Columns.Select(c => c.Alias));
        Assert.Equal([0, 1], plan.Columns.Select(c => c.Table));
    }

    [Fact]
    public void Views_can_be_used_like_tables()
    {
        var plan = TableViewPlanner.Plan(Schema, new TableViewDefinition(T("regions", "label")));

        Assert.Equal("regions", plan.Tables[0].Name);
    }

    [Fact]
    public void Rejects_a_missing_definition_or_main_table()
    {
        Assert.Contains("main table", Fails(null));
        Assert.Contains("main table", Fails(new TableViewDefinition(null!)));
    }

    [Fact]
    public void Rejects_a_table_that_is_not_in_the_data_source()
    {
        var message = Fails(new TableViewDefinition(T("secrets", "id")));

        Assert.Contains("'public.secrets' was not found", message);
    }

    [Fact]
    public void Rejects_a_field_the_table_does_not_have()
    {
        var message = Fails(new TableViewDefinition(T("orders", "id", "password")));

        Assert.Contains("no field 'password'", message);
    }

    [Fact]
    public void Needs_at_least_one_field()
    {
        Assert.Contains("at least one field", Fails(new TableViewDefinition(T("orders"))));
    }

    [Fact]
    public void A_join_needs_conditions_that_exist_and_point_backwards()
    {
        var noConditions = new TableViewDefinition(T("orders", "id"), [new TableViewJoin(JoinType.Inner, T("customers", "name"), [])]);
        Assert.Contains("relates to the earlier tables", Fails(noConditions));

        var forward = new TableViewDefinition(
            T("orders", "id"),
            [new TableViewJoin(JoinType.Inner, T("customers", "name"), [new JoinCondition(1, "id", "id")])]);
        Assert.Contains("earlier steps", Fails(forward));

        var badLeft = new TableViewDefinition(
            T("orders", "id"),
            [new TableViewJoin(JoinType.Inner, T("customers", "name"), [new JoinCondition(0, "nope", "id")])]);
        Assert.Contains("no field 'nope'", Fails(badLeft));

        var badRight = new TableViewDefinition(
            T("orders", "id"),
            [new TableViewJoin(JoinType.Inner, T("customers", "name"), [new JoinCondition(0, "customer_id", "nope")])]);
        Assert.Contains("no field 'nope'", Fails(badRight));
    }

    [Fact]
    public void A_join_can_use_several_conditions_but_not_too_many()
    {
        var some = Enumerable.Repeat(new JoinCondition(0, "customer_id", "id"), 2).ToList();
        TableViewPlanner.Plan(Schema, new TableViewDefinition(T("orders", "id"), [new TableViewJoin(JoinType.Inner, T("customers", "name"), some)]));

        var many = Enumerable.Repeat(new JoinCondition(0, "customer_id", "id"), TableViewPlanner.MaxConditionsPerJoin + 1).ToList();
        Assert.Contains("conditions", Fails(new TableViewDefinition(T("orders", "id"),
            [new TableViewJoin(JoinType.Inner, T("customers", "name"), many)])));
    }

    [Fact]
    public void Limits_the_number_of_joins()
    {
        var join = new TableViewJoin(JoinType.Inner, T("customers", "name"), [new JoinCondition(0, "customer_id", "id")]);
        var definition = new TableViewDefinition(T("orders", "id"), Enumerable.Repeat(join, TableViewPlanner.MaxJoins + 1).ToList());

        Assert.Contains("at most", Fails(definition));
    }

    [Fact]
    public void Rejects_an_undefined_join_type()
    {
        var definition = new TableViewDefinition(
            T("orders", "id"),
            [new TableViewJoin((JoinType)99, T("customers", "name"), [new JoinCondition(0, "customer_id", "id")])]);

        Assert.Contains("join type", Fails(definition));
    }

    [Fact]
    public void The_first_steps_of_a_definition_can_be_run_on_their_own()
    {
        var join = new TableViewJoin(JoinType.Inner, T("customers", "name"), [new JoinCondition(0, "customer_id", "id")]);
        var definition = new TableViewDefinition(T("orders", "id"), [join, join]);

        Assert.Empty(definition.UpToStep(0).Joins!);
        Assert.Single(definition.UpToStep(1).Joins!);
    }

    [Fact]
    public void A_definition_survives_json()
    {
        var definition = new TableViewDefinition(
            T("orders", "id"),
            [new TableViewJoin(JoinType.Left, T("customers", "name"), [new JoinCondition(0, "customer_id", "id")])]);

        var back = TableViewDefinition.FromJson(definition.ToJson())!;

        Assert.Equal(JoinType.Left, back.Joins![0].Type);
        Assert.Equal(["id"], back.Main.Fields);
        Assert.Contains("\"type\":\"Left\"", definition.ToJson());
    }
}

public class TableViewDatasetHandlerTests
{
    private readonly FakeDatasetRepo _datasets = new();
    private readonly FakeDataSourceRepo _sources = new();
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeUow _uow = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly FakeConnector _connector = new();
    private readonly FakeValidator _validator = new();
    private readonly FakeTableViewRenderer _renderer = new();
    private readonly Guid _org = Guid.NewGuid();
    private readonly DataSource _source;

    public TableViewDatasetHandlerTests()
    {
        _source = DataSource.Create(_org, "Shop", DataSourceType.PostgreSql, new FakeSecrets().Protect("Host=db"), DateTime.UtcNow);
        _sources.Items.Add(_source);
        _connector.Schema = new DatabaseSchema(
            [new SchemaNode("public",
            [
                new TableSchema("orders", "Table", [new("id", "integer", false), new("customer_id", "integer", false)]),
                new TableSchema("customers", "Table", [new("id", "integer", false), new("name", "text", true)]),
            ])],
            false);
        _connector.DescribedColumns = [new("id", "integer"), new("name", "text")];
    }

    private OrganizationAccess Access() => new(_orgs, _user);
    private DataSourceConnectors Connectors() => new([_connector]);
    private TableViewSqlRenderers Renderers() => new([_renderer]);
    private void Join(OrganizationRole role) =>
        _orgs.Members.Add(OrganizationMember.Create(_org, _user.UserId!.Value, role, DateTime.UtcNow));

    private CreateDatasetHandler Create() =>
        new(_datasets, _sources, new FakeSecrets(), Connectors(), Renderers(), _validator, _uow, new FixedClock(), Access());

    private UpdateDatasetHandler Update() =>
        new(_datasets, _sources, new FakeSecrets(), Connectors(), Renderers(), _validator, _uow, new FixedClock(), Access());

    private PreviewTableViewHandler PreviewTv() =>
        new(_sources, new FakeSecrets(), Connectors(), Renderers(), _validator, Access());

    private static TableViewDefinition Definition() => new(
        new TableViewTable("public", "orders", ["id"]),
        [new TableViewJoin(JoinType.Inner, new TableViewTable("public", "customers", ["name"]), [new JoinCondition(0, "customer_id", "id")])]);

    private CreateDatasetRequest Request(TableViewDefinition? definition = null, string? sql = null) =>
        new(_source.Id, "Orders with customers", null, sql, DatasetKind.TableView, definition ?? Definition());

    [Fact]
    public async Task Creates_a_table_view_whose_sql_is_generated_validated_and_described()
    {
        Join(OrganizationRole.Editor);

        var dto = await Create().HandleAsync(_org, Request(sql: "SELECT 'ignored'"));

        var stored = Assert.Single(_datasets.Items);
        Assert.Equal(DatasetKind.TableView, stored.Kind);
        Assert.Equal("VALIDATED:SELECT t0.id AS id, t1.name AS name FROM public.orders JOIN public.customers", stored.Sql);
        Assert.Equal(["id", "name"], dto.Columns.Select(c => c.Name));
        Assert.Equal(DatasetKind.TableView, dto.Kind);
        Assert.Equal("customers", dto.Definition!.Joins![0].Table.Name);
        Assert.NotNull(stored.DefinitionJson);
    }

    [Fact]
    public async Task A_table_view_that_names_a_missing_table_is_rejected_and_not_saved()
    {
        Join(OrganizationRole.Editor);
        var bad = new TableViewDefinition(new TableViewTable("public", "secrets", ["id"]));

        await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(_org, Request(bad)));

        Assert.Empty(_datasets.Items);
    }

    [Fact]
    public async Task A_sql_dataset_cannot_carry_a_definition()
    {
        Join(OrganizationRole.Editor);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Create().HandleAsync(_org, new CreateDatasetRequest(_source.Id, "x", null, "SELECT 1", DatasetKind.Sql, Definition())));
    }

    [Fact]
    public async Task Updating_a_table_view_regenerates_its_sql_and_keeps_its_kind()
    {
        Join(OrganizationRole.Editor);
        var created = await Create().HandleAsync(_org, Request());
        var changed = Definition() with { Main = new TableViewTable("public", "orders", ["id", "customer_id"]) };
        _connector.DescribedColumns = [new("id", "integer"), new("customer_id", "integer"), new("name", "text")];

        var dto = await Update().HandleAsync(_org, created.Id, new UpdateDatasetRequest("Renamed", null, "SELECT 'ignored'", changed));

        Assert.Equal(DatasetKind.TableView, dto.Kind);
        Assert.Contains("t0.customer_id AS customer_id", dto.Sql);
        Assert.Equal(3, dto.Columns.Count);
    }

    [Fact]
    public async Task A_sql_dataset_cannot_be_given_a_definition_on_update()
    {
        Join(OrganizationRole.Editor);
        var created = await Create().HandleAsync(_org, new CreateDatasetRequest(_source.Id, "Plain", null, "SELECT id, name FROM t"));

        await Assert.ThrowsAsync<ValidationException>(() =>
            Update().HandleAsync(_org, created.Id, new UpdateDatasetRequest("Plain", null, null, Definition())));
    }

    [Fact]
    public async Task Viewers_see_neither_the_sql_nor_the_definition_of_a_table_view()
    {
        Join(OrganizationRole.Editor);
        await Create().HandleAsync(_org, Request());
        _orgs.Members.Clear();
        Join(OrganizationRole.Viewer);

        var dto = Assert.Single(await new ListDatasetsHandler(_datasets, Access()).HandleAsync(_org));

        Assert.Null(dto.Sql);
        Assert.Null(dto.Definition);
        Assert.Equal(DatasetKind.TableView, dto.Kind);
    }

    [Fact]
    public async Task Preview_runs_a_definition_without_saving_it_and_returns_the_sql_it_used()
    {
        Join(OrganizationRole.Editor);

        var result = await PreviewTv().HandleAsync(_org, _source.Id, new PreviewTableViewRequest(Definition(), 20));

        Assert.Empty(_datasets.Items);
        Assert.Equal(1, _connector.PreviewCalls);
        Assert.Equal(20, _connector.LastMaxRows);
        Assert.StartsWith("VALIDATED:SELECT t0.id AS id", result.Sql);
    }

    [Fact]
    public async Task Preview_is_for_editors_only()
    {
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            PreviewTv().HandleAsync(_org, _source.Id, new PreviewTableViewRequest(Definition(), null)));
    }
}
