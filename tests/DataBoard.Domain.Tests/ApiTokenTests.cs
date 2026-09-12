using DataBoard.Domain.Users;

namespace DataBoard.Domain.Tests;

public class ApiTokenTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string Hash = new('b', ApiToken.TokenHashLength);

    private static ApiToken Create(
        string name = "Claude Desktop", ApiTokenScope scope = ApiTokenScope.Write, DateTime? expires = null, string? hash = null, Guid? user = null) =>
        ApiToken.Create(user ?? Guid.NewGuid(), name, hash ?? Hash, scope, Now, expires);

    [Fact]
    public void Keeps_what_it_was_made_with_and_trims_the_name()
    {
        var user = Guid.NewGuid();

        var token = Create("  Claude Desktop  ", ApiTokenScope.Read, Now.AddDays(90), user: user);

        Assert.Equal(user, token.UserId);
        Assert.Equal("Claude Desktop", token.Name);
        Assert.Equal(Hash, token.TokenHash);
        Assert.Equal(ApiTokenScope.Read, token.Scope);
        Assert.Equal(Now, token.CreatedUtc);
        Assert.Equal(Now.AddDays(90), token.ExpiresUtc);
        Assert.Null(token.LastUsedUtc);
    }

    [Fact]
    public void Works_until_it_expires_and_not_after()
    {
        var token = Create(expires: Now.AddDays(1));

        Assert.True(token.IsActive(Now));
        Assert.False(token.IsActive(Now.AddDays(1)));
        Assert.False(token.IsActive(Now.AddDays(2)));
    }

    [Fact]
    public void Without_an_expiry_it_works_until_it_is_revoked() =>
        Assert.True(Create().IsActive(Now.AddYears(50)));

    [Fact]
    public void Cannot_be_made_already_expired() =>
        Assert.Throws<ArgumentException>(() => Create(expires: Now));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Needs_a_name(string name) =>
        Assert.Throws<ArgumentException>(() => Create(name));

    [Fact]
    public void Limits_the_name_length()
    {
        Assert.NotNull(Create(new string('x', ApiToken.MaxNameLength)));
        Assert.Throws<ArgumentException>(() => Create(new string('x', ApiToken.MaxNameLength + 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    public void Needs_a_full_length_token_hash(string hash) =>
        Assert.Throws<ArgumentException>(() => Create(hash: hash));

    [Fact]
    public void Needs_a_user_and_a_known_scope()
    {
        Assert.Throws<ArgumentException>(() => Create(user: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Create(scope: (ApiTokenScope)99));
    }

    [Fact]
    public void Notes_a_use_only_now_and_then()
    {
        var token = Create();

        Assert.True(token.MarkUsed(Now));
        Assert.False(token.MarkUsed(Now.AddMinutes(4))); // too soon to write again
        Assert.Equal(Now, token.LastUsedUtc);
        Assert.True(token.MarkUsed(Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(5), token.LastUsedUtc);
    }
}
