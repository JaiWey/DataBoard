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
    public void Keeps_the_encrypted_secret_when_it_is_given_and_none_otherwise()
    {
        var withSecret = DashboardShare.Create(Guid.NewGuid(), Guid.NewGuid(), Hash, Guid.NewGuid(), Now, null, null, "CfDJ8-encrypted");

        Assert.Equal("CfDJ8-encrypted", withSecret.EncryptedToken);
        Assert.Null(Create().EncryptedToken); // a link made before secrets were kept
    }

    [Fact]
    public void Refuses_an_encrypted_secret_that_is_too_long()
    {
        var tooLong = new string('x', DashboardShare.MaxEncryptedTokenLength + 1);

        Assert.Throws<ArgumentException>(() => DashboardShare.Create(Guid.NewGuid(), Guid.NewGuid(), Hash, Guid.NewGuid(), Now, null, null, tooLong));
    }

    private static DashboardShare Limited(params Guid[] organizations) =>
        DashboardShare.Create(Guid.NewGuid(), Guid.NewGuid(), Hash, Guid.NewGuid(), Now, null, organizations);

    [Fact]
    public void Is_public_unless_organizations_are_given()
    {
        Assert.False(Create().IsRestricted);
        Assert.Empty(Create().AllowedOrganizationIds);
        Assert.False(Limited().IsRestricted);
    }

    [Fact]
    public void Limited_to_organizations_it_keeps_each_once()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var share = Limited(a, b, a);

        Assert.True(share.IsRestricted);
        Assert.Equal([a, b], share.AllowedOrganizationIds);
    }

    [Fact]
    public void An_empty_organization_or_too_many_are_refused()
    {
        Assert.Throws<ArgumentException>(() => Limited(Guid.NewGuid(), Guid.Empty));
        Assert.Throws<ArgumentException>(() =>
            Limited(Enumerable.Range(0, DashboardShare.MaxAllowedOrganizations + 1).Select(_ => Guid.NewGuid()).ToArray()));
        Assert.Equal(DashboardShare.MaxAllowedOrganizations,
            Limited(Enumerable.Range(0, DashboardShare.MaxAllowedOrganizations).Select(_ => Guid.NewGuid()).ToArray()).AllowedOrganizationIds.Length);
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
