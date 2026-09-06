using DataBoard.Domain.Datasets;

namespace DataBoard.Domain.Tests;

public class DatasetTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Source = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static readonly DatasetColumn[] Columns =
    [
        new("region", "text", ColumnRole.Dimension),
        new("amount", "numeric", ColumnRole.Measure),
    ];

    private static Dataset New(string name = "Sales", string? description = null, string sql = "SELECT 1", IEnumerable<DatasetColumn>? columns = null) =>
        Dataset.Create(Org, Source, name, description, sql, columns ?? Columns, User, Now);

    [Fact]
    public void Create_trims_name_and_keeps_columns_in_order()
    {
        var dataset = New("  Sales  ", "  weekly  ");

        Assert.Equal("Sales", dataset.Name);
        Assert.Equal("weekly", dataset.Description);
        Assert.Equal(["region", "amount"], dataset.Columns.Select(c => c.Name));
        Assert.Equal(Now, dataset.CreatedUtc);
        Assert.Equal(Now, dataset.UpdatedUtc);
    }

    [Fact]
    public void Blank_description_becomes_null() => Assert.Null(New(description: "   ").Description);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_blank_name(string name) => Assert.Throws<ArgumentException>(() => New(name));

    [Fact]
    public void Create_rejects_too_long_name_and_description()
    {
        Assert.Throws<ArgumentException>(() => New(new string('a', Dataset.MaxNameLength + 1)));
        Assert.Throws<ArgumentException>(() => New(description: new string('a', Dataset.MaxDescriptionLength + 1)));
    }

    [Fact]
    public void Create_rejects_blank_sql() => Assert.Throws<ArgumentException>(() => New(sql: " "));

    [Fact]
    public void Create_rejects_missing_ids()
    {
        Assert.Throws<ArgumentException>(() => Dataset.Create(Guid.Empty, Source, "n", null, "s", Columns, User, Now));
        Assert.Throws<ArgumentException>(() => Dataset.Create(Org, Guid.Empty, "n", null, "s", Columns, User, Now));
        Assert.Throws<ArgumentException>(() => Dataset.Create(Org, Source, "n", null, "s", Columns, Guid.Empty, Now));
    }

    [Fact]
    public void Columns_must_exist_be_unique_ignoring_case_and_have_valid_roles()
    {
        Assert.Throws<ArgumentException>(() => New(columns: []));
        Assert.Throws<ArgumentException>(() => New(columns: [new("a", "text", ColumnRole.Dimension), new("A", "text", ColumnRole.Dimension)]));
        Assert.Throws<ArgumentException>(() => New(columns: [new(" ", "text", ColumnRole.Dimension)]));
        Assert.Throws<ArgumentException>(() => New(columns: [new("a", "text", (ColumnRole)99)]));
    }

    [Fact]
    public void Too_many_columns_are_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            New(columns: Enumerable.Range(0, Dataset.MaxColumns + 1).Select(i => new DatasetColumn($"c{i}", "text", ColumnRole.Dimension))));

    [Fact]
    public void Update_replaces_everything_and_moves_the_updated_time_only()
    {
        var dataset = New();
        var later = Now.AddHours(1);

        dataset.Update("Renamed", "d", "SELECT 2", [new("x", "bigint", ColumnRole.Measure)], later);

        Assert.Equal("Renamed", dataset.Name);
        Assert.Equal("SELECT 2", dataset.Sql);
        Assert.Equal(["x"], dataset.Columns.Select(c => c.Name));
        Assert.Equal(Now, dataset.CreatedUtc);
        Assert.Equal(later, dataset.UpdatedUtc);
    }

    [Fact]
    public void A_failed_update_changes_nothing()
    {
        var dataset = New();

        Assert.Throws<ArgumentException>(() => dataset.Update("Renamed", null, "SELECT 2", [], Now.AddHours(1)));

        Assert.Equal("Sales", dataset.Name);
        Assert.Equal("SELECT 1", dataset.Sql);
        Assert.Equal(2, dataset.Columns.Count);
    }

    [Fact]
    public void A_dataset_is_a_sql_dataset_by_default()
    {
        var dataset = New();

        Assert.Equal(DatasetKind.Sql, dataset.Kind);
        Assert.Null(dataset.DefinitionJson);
    }

    [Fact]
    public void A_table_view_keeps_its_definition_and_needs_one()
    {
        var view = Dataset.Create(Org, Source, "View", null, "SELECT 1", Columns, User, Now, DatasetKind.TableView, "{\"main\":{}}");

        Assert.Equal(DatasetKind.TableView, view.Kind);
        Assert.Equal("{\"main\":{}}", view.DefinitionJson);
        Assert.Throws<ArgumentException>(() =>
            Dataset.Create(Org, Source, "View", null, "SELECT 1", Columns, User, Now, DatasetKind.TableView, null));
    }

    [Fact]
    public void A_sql_dataset_cannot_have_a_definition()
    {
        Assert.Throws<ArgumentException>(() =>
            Dataset.Create(Org, Source, "Plain", null, "SELECT 1", Columns, User, Now, DatasetKind.Sql, "{}"));
    }

    [Fact]
    public void Updating_a_table_view_replaces_its_definition_and_keeps_its_kind()
    {
        var view = Dataset.Create(Org, Source, "View", null, "SELECT 1", Columns, User, Now, DatasetKind.TableView, "{\"a\":1}");

        view.Update("View", null, "SELECT 2", Columns, Now, "{\"a\":2}");

        Assert.Equal(DatasetKind.TableView, view.Kind);
        Assert.Equal("{\"a\":2}", view.DefinitionJson);
        Assert.Throws<ArgumentException>(() => view.Update("View", null, "SELECT 2", Columns, Now, null));
    }
}
