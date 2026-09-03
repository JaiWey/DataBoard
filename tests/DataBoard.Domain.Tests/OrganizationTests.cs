using DataBoard.Domain.Organizations;

namespace DataBoard.Domain.Tests;

public class OrganizationTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_trims_name() =>
        Assert.Equal("Acme", Organization.Create("  Acme ", Now).Name);

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_blank_name(string name) =>
        Assert.Throws<ArgumentException>(() => Organization.Create(name, Now));

    [Fact]
    public void Create_rejects_too_long_name() =>
        Assert.Throws<ArgumentException>(() => Organization.Create(new string('a', Organization.MaxNameLength + 1), Now));

    [Theory]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Owner, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Viewer, true)]
    [InlineData(OrganizationRole.Editor, OrganizationRole.Editor, true)]
    [InlineData(OrganizationRole.Editor, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Editor, false)]
    public void HasAtLeast_follows_role_order(OrganizationRole actual, OrganizationRole required, bool expected)
    {
        var member = OrganizationMember.Create(Guid.NewGuid(), Guid.NewGuid(), actual, Now);

        Assert.Equal(expected, member.HasAtLeast(required));
    }

    [Fact]
    public void Member_requires_ids_and_known_role()
    {
        Assert.Throws<ArgumentException>(() => OrganizationMember.Create(Guid.Empty, Guid.NewGuid(), OrganizationRole.Viewer, Now));
        Assert.Throws<ArgumentException>(() => OrganizationMember.Create(Guid.NewGuid(), Guid.Empty, OrganizationRole.Viewer, Now));
        Assert.Throws<ArgumentException>(() => OrganizationMember.Create(Guid.NewGuid(), Guid.NewGuid(), (OrganizationRole)99, Now));
    }
}
