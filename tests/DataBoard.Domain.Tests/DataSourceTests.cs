using DataBoard.Domain.DataSources;

namespace DataBoard.Domain.Tests;

public class DataSourceTests
{
    private static readonly Guid Org = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_with_valid_values_trims_name()
    {
        var ds = DataSource.Create(Org, "  Sales DB ", DataSourceType.PostgreSql, "secret", Now);

        Assert.Equal("Sales DB", ds.Name);
        Assert.Equal(Org, ds.OrganizationId);
        Assert.NotEqual(Guid.Empty, ds.Id);
        Assert.Equal(Now, ds.CreatedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_name(string name) =>
        Assert.Throws<ArgumentException>(() => DataSource.Create(Org, name, DataSourceType.Csv, "x", Now));

    [Fact]
    public void Create_rejects_empty_organization() =>
        Assert.Throws<ArgumentException>(() => DataSource.Create(Guid.Empty, "n", DataSourceType.Csv, "x", Now));

    [Fact]
    public void Create_rejects_unknown_type() =>
        Assert.Throws<ArgumentException>(() => DataSource.Create(Org, "n", (DataSourceType)99, "x", Now));

    [Fact]
    public void Create_rejects_too_long_name() =>
        Assert.Throws<ArgumentException>(() =>
            DataSource.Create(Org, new string('a', DataSource.MaxNameLength + 1), DataSourceType.Csv, "x", Now));
}
