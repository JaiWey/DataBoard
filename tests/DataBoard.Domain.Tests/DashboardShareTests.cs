using DataBoard.Domain.Dashboards;

namespace DataBoard.Domain.Tests;

public class DashboardShareTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('a', DashboardShare.TokenHashLength);

    private static DashboardShare Create(DateTime? expires = null, string? hash = null, Guid? organization = null, Guid? dashboard = null, Guid? user = null) =>
        DashboardShare.Create(organization ?? Guid.NewGuid(), dashboard ?? Guid.NewGuid(), hash ?? Hash, user ?? Guid.NewGuid(), Now, expires);

    [Fact]
    public void Keeps_what_it_was_made_with()
    {
        var org = Guid.NewGuid();
        var dashboard = Guid.NewGuid();
        var user = Guid.NewGuid();

        var share = Create(Now.AddDays(30), organization: org, dashboard: dashboard, user: user);

        Assert.Equal((org, dashboard, user), (share.OrganizationId, share.DashboardId, share.CreatedByUserId));
        Assert.Equal(Hash, share.TokenHash);
        Assert.Equal(Now, share.CreatedUtc);
        Assert.Equal(Now.AddDays(30), share.ExpiresUtc);
    }

    [Fact]
    public void Works_until_it_expires_and_not_after()
    {
        var share = Create(Now.AddDays(1));

        Assert.True(share.IsActive(Now));
        Assert.True(share.IsActive(Now.AddDays(1).AddTicks(-1)));
        Assert.False(share.IsActive(Now.AddDays(1)));
        Assert.False(share.IsActive(Now.AddDays(2)));
    }

    [Fact]
    public void Without_an_expiry_it_works_until_it_is_revoked()
    {
        var share = Create();

        Assert.Null(share.ExpiresUtc);
        Assert.True(share.IsActive(Now.AddYears(50)));
    }

    [Fact]
    public void Cannot_be_made_already_expired() =>
        Assert.Throws<ArgumentException>(() => Create(Now));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Needs_a_full_length_token_hash(string hash) =>
        Assert.Throws<ArgumentException>(() => Create(hash: hash));

    [Fact]
    public void Needs_an_organization_a_dashboard_and_a_user()
    {
        Assert.Throws<ArgumentException>(() => Create(organization: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(dashboard: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(user: Guid.Empty));
    }
}
