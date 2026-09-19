using DataBoard.Domain.Datasets;

namespace DataBoard.Domain.Tests;

public class DatasetFolderTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

    private static DatasetFolder New(string name = "Sales", Guid? parent = null) =>
        DatasetFolder.Create(Guid.NewGuid(), parent, name, Guid.NewGuid(), Now);

    [Fact]
    public void Trims_the_name_and_keeps_its_parent()
    {
        var parent = Guid.NewGuid();

        var folder = New("  Sales  ", parent);

        Assert.Equal("Sales", folder.Name);
        Assert.Equal(parent, folder.ParentId);
        Assert.Equal(Now, folder.CreatedUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void Refuses_a_bad_name(string name) => Assert.Throws<ArgumentException>(() => New(name));

    [Fact]
    public void Refuses_a_name_over_the_limit_but_takes_one_at_it()
    {
        Assert.Equal(DatasetFolder.MaxNameLength, New(new string('x', DatasetFolder.MaxNameLength)).Name.Length);
        Assert.Throws<ArgumentException>(() => New(new string('x', DatasetFolder.MaxNameLength + 1)));
    }

    [Fact]
    public void Needs_an_organization_and_a_user()
    {
        Assert.Throws<ArgumentException>(() => DatasetFolder.Create(Guid.Empty, null, "A", Guid.NewGuid(), Now));
        Assert.Throws<ArgumentException>(() => DatasetFolder.Create(Guid.NewGuid(), null, "A", Guid.Empty, Now));
    }

    [Fact]
    public void Renames_and_moves_and_records_when()
    {
        var folder = New();
        var later = Now.AddHours(1);

        folder.Rename(" Ops ", later);
        var parent = Guid.NewGuid();
        folder.MoveTo(parent, later);

        Assert.Equal("Ops", folder.Name);
        Assert.Equal(parent, folder.ParentId);
        Assert.Equal(later, folder.UpdatedUtc);
    }

    [Fact]
    public void Cannot_be_its_own_parent_and_keeps_its_name_after_a_bad_rename()
    {
        var folder = New();

        Assert.Throws<ArgumentException>(() => folder.MoveTo(folder.Id, Now));
        Assert.Throws<ArgumentException>(() => folder.Rename("a/b", Now));

        Assert.Null(folder.ParentId);
        Assert.Equal("Sales", folder.Name);
    }
}
