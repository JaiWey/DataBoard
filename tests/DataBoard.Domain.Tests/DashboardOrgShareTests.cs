using DataBoard.Domain.Dashboards;

namespace DataBoard.Domain.Tests;

public class DashboardOrgShareTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Keeps_what_it_was_made_with()
    {
        var org = Guid.NewGuid();
        var dashboard = Guid.NewGuid();
        var target = Guid.NewGuid();
        var user = Guid.NewGuid();

        var share = DashboardOrgShare.Create(org, dashboard, target, user, Now);

        Assert.Equal((org, dashboard, target, user, Now), (share.OrganizationId, share.DashboardId, share.TargetOrganizationId, share.SharedByUserId, share.CreatedUtc));
    }

    [Fact]
    public void A_dashboard_is_not_shared_with_the_organization_it_is_already_in()
    {
        var org = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => DashboardOrgShare.Create(org, Guid.NewGuid(), org, Guid.NewGuid(), Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Everything_has_to_be_named(int missing)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        ids[missing] = Guid.Empty;

        Assert.Throws<ArgumentException>(() => DashboardOrgShare.Create(ids[0], ids[1], ids[2], ids[3], Now));
    }
}
