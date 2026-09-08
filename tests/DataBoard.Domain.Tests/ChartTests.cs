using DataBoard.Domain.Charts;

namespace DataBoard.Domain.Tests;

public class ChartTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly Guid Dataset = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    private static Chart New(string name = "Revenue", string? description = null, ChartType type = ChartType.Bar, string json = "{}", Guid? dataset = null) =>
        Chart.Create(Org, dataset ?? Dataset, name, description, type, json, User, Now);

    [Fact]
    public void Create_trims_the_name_and_keeps_what_was_given()
    {
        var chart = New("  Revenue  ", "  per region  ", ChartType.Donut, "{\"a\":1}");

        Assert.Equal("Revenue", chart.Name);
        Assert.Equal("per region", chart.Description);
        Assert.Equal(ChartType.Donut, chart.Type);
        Assert.Equal("{\"a\":1}", chart.DefinitionJson);
        Assert.Equal(Dataset, chart.DatasetId);
        Assert.Equal(Now, chart.CreatedUtc);
        Assert.Equal(Now, chart.UpdatedUtc);
    }

    [Fact]
    public void A_blank_description_becomes_null() => Assert.Null(New(description: "   ").Description);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_is_required(string name) => Assert.Throws<ArgumentException>(() => New(name));

    [Fact]
    public void A_name_and_description_have_a_maximum_length()
    {
        Assert.Throws<ArgumentException>(() => New(new string('x', Chart.MaxNameLength + 1)));
        Assert.Throws<ArgumentException>(() => New(description: new string('x', Chart.MaxDescriptionLength + 1)));
        New(new string('x', Chart.MaxNameLength)); // exactly the maximum is fine
    }

    [Fact]
    public void A_dataset_the_organization_the_user_and_a_definition_are_required()
    {
        Assert.Throws<ArgumentException>(() => New(dataset: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Chart.Create(Guid.Empty, Dataset, "x", null, ChartType.Bar, "{}", User, Now));
        Assert.Throws<ArgumentException>(() => Chart.Create(Org, Dataset, "x", null, ChartType.Bar, "{}", Guid.Empty, Now));
        Assert.Throws<ArgumentException>(() => New(json: " "));
    }

    [Fact]
    public void An_undefined_chart_type_is_rejected() => Assert.Throws<ArgumentException>(() => New(type: (ChartType)99));

    [Fact]
    public void Update_replaces_everything_and_moves_the_updated_time()
    {
        var chart = New();
        var later = Now.AddHours(1);
        var otherDataset = Guid.NewGuid();

        chart.Update(otherDataset, "Costs", null, ChartType.Number, "{\"b\":2}", later);

        Assert.Equal(otherDataset, chart.DatasetId);
        Assert.Equal("Costs", chart.Name);
        Assert.Equal(ChartType.Number, chart.Type);
        Assert.Equal("{\"b\":2}", chart.DefinitionJson);
        Assert.Equal(Now, chart.CreatedUtc);
        Assert.Equal(later, chart.UpdatedUtc);
    }
}
