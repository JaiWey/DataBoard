using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Folders;
using DataBoard.Domain.Organizations;

namespace DataBoard.Domain.Tests;

public class AccessRuleTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Someone = Guid.NewGuid();

    [Fact]
    public void An_open_rule_lets_everybody_in()
    {
        Assert.True(AccessRule.Open.IsOpen);
        foreach (var role in Enum.GetValues<OrganizationRole>())
            Assert.True(AccessRule.Open.Allows(role, Someone));
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Viewer, true)]
    [InlineData(OrganizationRole.Editor, OrganizationRole.Viewer, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Viewer, true)]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Editor, false)]
    [InlineData(OrganizationRole.Editor, OrganizationRole.Editor, true)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Editor, true)]
    [InlineData(OrganizationRole.Viewer, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.Editor, OrganizationRole.Owner, false)]
    [InlineData(OrganizationRole.Owner, OrganizationRole.Owner, true)]
    public void A_minimum_role_lets_in_that_role_and_above(OrganizationRole person, OrganizationRole minimum, bool allowed)
    {
        var rule = AccessRule.Create(minimum, null);

        Assert.False(rule.IsOpen);
        Assert.Equal(allowed, rule.Allows(person, Someone));
    }

    [Fact]
    public void A_listed_person_gets_in_whatever_their_role_and_an_unlisted_one_with_the_same_role_does_not()
    {
        var rule = AccessRule.Create(null, [Someone]);

        Assert.True(rule.Allows(OrganizationRole.Viewer, Someone));
        Assert.False(rule.Allows(OrganizationRole.Viewer, Guid.NewGuid()));
        Assert.False(rule.Allows(OrganizationRole.Editor, Guid.NewGuid()));
    }

    [Fact]
    public void Owners_always_get_in_so_a_rule_cannot_lock_the_organization_out()
    {
        Assert.True(AccessRule.Create(null, [Guid.NewGuid()]).Allows(OrganizationRole.Owner, Someone));
    }

    [Fact]
    public void A_role_and_a_list_together_let_in_either()
    {
        var rule = AccessRule.Create(OrganizationRole.Editor, [Someone]);

        Assert.True(rule.Allows(OrganizationRole.Viewer, Someone));
        Assert.True(rule.Allows(OrganizationRole.Editor, Guid.NewGuid()));
        Assert.False(rule.Allows(OrganizationRole.Viewer, Guid.NewGuid()));
    }

    [Fact]
    public void Each_person_is_listed_once()
    {
        Assert.Single(AccessRule.Create(null, [Someone, Someone]).MemberIds);
    }

    [Fact]
    public void Nobody_empty_an_unknown_role_or_too_many_people_make_no_rule()
    {
        Assert.Throws<ArgumentException>(() => AccessRule.Create((OrganizationRole)0, null));
        Assert.Throws<ArgumentException>(() => AccessRule.Create((OrganizationRole)9, null));
        Assert.Throws<ArgumentException>(() => AccessRule.Create(null, [Guid.Empty]));
        Assert.Throws<ArgumentException>(() => AccessRule.Create(null, Enumerable.Range(0, AccessRule.MaxMembers + 1).Select(_ => Guid.NewGuid())));
        Assert.Equal(AccessRule.MaxMembers, AccessRule.Create(null, Enumerable.Range(0, AccessRule.MaxMembers).Select(_ => Guid.NewGuid())).MemberIds.Count);
    }

    [Fact]
    public void A_dashboard_and_a_folder_start_open_and_keep_the_rule_they_are_given()
    {
        var dashboard = Dashboard.Create(Guid.NewGuid(), "D", null, "{}", Guid.NewGuid(), Now);
        var folder = Folder.Create(Guid.NewGuid(), FolderKind.Dashboard, null, "F", Guid.NewGuid(), Now);
        Assert.True(dashboard.Access.IsOpen);
        Assert.True(folder.Access.IsOpen);

        var rule = AccessRule.Create(OrganizationRole.Editor, [Someone]);
        dashboard.SetAccess(rule);
        folder.SetAccess(rule);

        Assert.Equal(OrganizationRole.Editor, dashboard.Access.MinRole);
        Assert.Equal([Someone], folder.Access.MemberIds);
        Assert.Equal(Now, dashboard.UpdatedUtc); // who may see it is not an edit of what it holds

        dashboard.SetAccess(AccessRule.Open);
        Assert.True(dashboard.Access.IsOpen);
    }

    [Fact]
    public void Setting_a_bad_rule_changes_nothing()
    {
        var dashboard = Dashboard.Create(Guid.NewGuid(), "D", null, "{}", Guid.NewGuid(), Now);
        dashboard.SetAccess(AccessRule.Create(OrganizationRole.Editor, null));

        Assert.Throws<ArgumentException>(() => dashboard.SetAccess(new AccessRule((OrganizationRole)9, [])));

        Assert.Equal(OrganizationRole.Editor, dashboard.Access.MinRole);
    }
}
