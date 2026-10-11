using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static DataBoard.Server.IntegrationTests.ApiHelpers;

namespace DataBoard.Server.IntegrationTests;

/// <summary>Copying a dashboard, with its charts and datasets, into another organization the person belongs to.</summary>
[Collection(ApiCollection.Name)]
public class DashboardCopyToOrganizationApiTests(ApiFactory factory)
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private sealed record Setup(HttpClient Owner, string Email, Guid From, Guid To, Guid Dashboard, Guid Chart);

    private static string Orgs(Guid org, string path) => $"/api/organizations/{org}/{path}";

    private async Task<JsonElement> Read(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<Guid> DataSourceAsync(HttpClient client, Guid org, string name)
    {
        var made = await Read(await client.PostAsJsonAsync(Orgs(org, "data-sources"),
            new { name, type = "PostgreSql", connectionString = factory.TargetConnectionString() }));
        return made.GetProperty("id").GetGuid();
    }

    /// <summary>One person with two organizations. "From" has a data source, a dataset, a chart and a dashboard with a picture and a background.</summary>
    private async Task<Setup> NewSetupAsync(string? dataSourceNameInTarget = "Shop DB")
    {
        var (owner, email, _) = await NewUserAsync(factory);
        var from = (await CreateOrgAsync(owner, "From")).Id;
        var to = (await CreateOrgAsync(owner, "To")).Id;

        var source = await DataSourceAsync(owner, from, "Shop DB");
        if (dataSourceNameInTarget is not null) await DataSourceAsync(owner, to, dataSourceNameInTarget);

        var dataset = (await Read(await owner.PostAsJsonAsync(Orgs(from, "datasets"),
            new { dataSourceId = source, name = "Orders", sql = "SELECT id, region, price FROM orders" }))).GetProperty("id").GetGuid();
        var chart = (await Read(await owner.PostAsJsonAsync(Orgs(from, "charts"), new
        {
            name = "Revenue", datasetId = dataset, type = "Number",
            definition = new { query = new { measures = new[] { new { aggregation = "Sum", column = "price", alias = "n" } } } },
        }))).GetProperty("id").GetGuid();

        var dashboard = (await Read(await owner.PostAsJsonAsync(Orgs(from, "dashboards"), new
        {
            name = "Sales", description = "Weekly",
            definition = new
            {
                widgets = new object[]
                {
                    new { chartId = chart, layout = new { x = 0, y = 0, w = 6, h = 5 } },
                    new { kind = "Text", text = "Hello", layout = new { x = 6, y = 0, w = 6, h = 2 } },
                },
                filters = new object[] { new { label = "Region", column = "region", @operator = "In" } },
            },
        }))).GetProperty("id").GetGuid();

        var picture = new MultipartFormDataContent { { new ByteArrayContent(Png), "File", "a.png" } };
        picture.Add(new StringContent("cover"), "Fit");
        await Read(await owner.PostAsync(Orgs(from, $"dashboards/{dashboard}/image-widgets"), picture));
        var background = new MultipartFormDataContent { { new ByteArrayContent(Png), "File", "bg.png" } };
        background.Add(new StringContent("tile"), "Fit");
        background.Add(new StringContent("40"), "Opacity");
        await Read(await owner.PutAsync(Orgs(from, $"dashboards/{dashboard}/background"), background));

        return new Setup(owner, email, from, to, dashboard, chart);
    }

    private static Task<HttpResponseMessage> CopyAsync(HttpClient client, Setup s) =>
        client.PostAsync(Orgs(s.From, $"dashboards/{s.Dashboard}/copy-to/{s.To}"), null);

    private static async Task<JsonElement[]> NamesAsync(HttpClient client, Guid org, string path) =>
        (await client.GetFromJsonAsync<JsonElement>(Orgs(org, path), Json)).EnumerateArray().ToArray();

    [Fact]
    public async Task The_dashboard_arrives_with_its_charts_datasets_pictures_and_filters()
    {
        var s = await NewSetupAsync("shop db"); // the data source is found by name, whatever the case

        var result = await Read(await CopyAsync(s.Owner, s));

        Assert.Equal(1, result.GetProperty("newDatasets").GetInt32());
        Assert.Equal(1, result.GetProperty("newCharts").GetInt32());
        Assert.Empty(result.GetProperty("missingDataSources").EnumerateArray());
        Assert.Empty(result.GetProperty("skippedCharts").EnumerateArray());

        var dashboard = result.GetProperty("dashboard");
        Assert.Equal(dashboard.GetProperty("id").GetGuid(), Assert.Single(await NamesAsync(s.Owner, s.To, "dashboards")).GetProperty("id").GetGuid());
        Assert.Equal("Sales", dashboard.GetProperty("name").GetString());
        Assert.Equal("Weekly", dashboard.GetProperty("description").GetString());

        var targetChart = Assert.Single(await NamesAsync(s.Owner, s.To, "charts"));
        Assert.Equal("Revenue", targetChart.GetProperty("name").GetString());
        Assert.NotEqual(s.Chart, targetChart.GetProperty("id").GetGuid());
        Assert.Equal("Orders", Assert.Single(await NamesAsync(s.Owner, s.To, "datasets")).GetProperty("name").GetString());

        var widgets = dashboard.GetProperty("definition").GetProperty("widgets").EnumerateArray().ToArray();
        Assert.Equal(3, widgets.Length); // chart, text, picture
        Assert.Equal(targetChart.GetProperty("id").GetGuid(),
            widgets.Single(w => w.GetProperty("kind").GetString() == "Chart").GetProperty("chartId").GetGuid());
        Assert.Single(dashboard.GetProperty("definition").GetProperty("filters").EnumerateArray());

        var newId = dashboard.GetProperty("id").GetGuid();
        var image = widgets.Single(w => w.GetProperty("kind").GetString() == "Image").GetProperty("id").GetGuid();
        Assert.Equal(Png, await (await s.Owner.GetAsync(Orgs(s.To, $"dashboards/{newId}/image-widgets/{image}/image"))).Content.ReadAsByteArrayAsync());
        Assert.Equal(Png, await (await s.Owner.GetAsync(Orgs(s.To, $"dashboards/{newId}/background"))).Content.ReadAsByteArrayAsync());

        // The original is untouched.
        Assert.Single(await NamesAsync(s.Owner, s.From, "dashboards"));
    }

    [Fact]
    public async Task Copying_again_reuses_what_is_there_and_names_the_dashboard_again()
    {
        var s = await NewSetupAsync();
        await Read(await CopyAsync(s.Owner, s));

        var second = await Read(await CopyAsync(s.Owner, s));

        Assert.Equal(0, second.GetProperty("newDatasets").GetInt32());
        Assert.Equal(0, second.GetProperty("newCharts").GetInt32());
        Assert.Equal("Sales (2)", second.GetProperty("dashboard").GetProperty("name").GetString());
        Assert.Single(await NamesAsync(s.Owner, s.To, "charts"));
        Assert.Single(await NamesAsync(s.Owner, s.To, "datasets"));
    }

    [Fact]
    public async Task Without_the_data_source_there_the_charts_are_left_out_and_reported()
    {
        var s = await NewSetupAsync(dataSourceNameInTarget: "Something else");

        var result = await Read(await CopyAsync(s.Owner, s));

        Assert.Equal(["Shop DB"], result.GetProperty("missingDataSources").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(["Revenue"], result.GetProperty("skippedCharts").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(0, result.GetProperty("newDatasets").GetInt32());
        Assert.Equal(0, result.GetProperty("newCharts").GetInt32());
        var kinds = result.GetProperty("dashboard").GetProperty("definition").GetProperty("widgets").EnumerateArray()
            .Select(w => w.GetProperty("kind").GetString()).Order().ToArray();
        Assert.Equal(["Image", "Text"], kinds); // the chart widget is gone, the rest came
        Assert.Empty(await NamesAsync(s.Owner, s.To, "charts"));
        Assert.Empty(await NamesAsync(s.Owner, s.To, "datasets"));
    }

    [Fact]
    public async Task Copying_into_the_same_organization_is_refused()
    {
        var s = await NewSetupAsync();

        var response = await s.Owner.PostAsync(Orgs(s.From, $"dashboards/{s.Dashboard}/copy-to/{s.From}"), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Only_an_editor_of_both_organizations_may_copy()
    {
        var s = await NewSetupAsync();
        var (viewer, viewerEmail, _) = await NewUserAsync(factory);
        (await s.Owner.PostAsJsonAsync(Orgs(s.From, "members"), new { email = viewerEmail, role = "Editor" })).EnsureSuccessStatusCode();
        (await s.Owner.PostAsJsonAsync(Orgs(s.To, "members"), new { email = viewerEmail, role = "Viewer" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await CopyAsync(viewer, s)).StatusCode); // only a Viewer where it would land

        var (stranger, _, _) = await NewUserAsync(factory);
        Assert.Equal(HttpStatusCode.NotFound, (await CopyAsync(stranger, s)).StatusCode); // not in either

        Assert.Empty(await NamesAsync(s.Owner, s.To, "dashboards"));
    }
}
