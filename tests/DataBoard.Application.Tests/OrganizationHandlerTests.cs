using DataBoard.Application.Common;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

public class OrganizationHandlerTests
{
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeIdentity _identity = new();
    private readonly FakeUow _uow = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());

    private OrganizationAccess Access() => new(_orgs, _user);

    private CreateOrganizationHandler Create() => new(_orgs, _uow, Access(), new FixedClock());

    private AddMemberHandler AddMember() => new(_orgs, _identity, _uow, Access(), new FixedClock());

    [Fact]
    public async Task Creator_becomes_owner()
    {
        var dto = await Create().HandleAsync(new CreateOrganizationRequest("  Acme  "));

        Assert.Equal("Acme", dto.Name);
        Assert.Equal(OrganizationRole.Owner, dto.Role);
        var member = Assert.Single(_orgs.Members);
        Assert.Equal(_user.UserId, member.UserId);
        Assert.Equal(OrganizationRole.Owner, member.Role);
    }

    [Fact]
    public async Task Blank_name_is_a_validation_error()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Create().HandleAsync(new CreateOrganizationRequest(" ")));
        Assert.Empty(_orgs.Organizations);
    }

    [Fact]
    public async Task Anonymous_cannot_create()
    {
        _user.UserId = null;

        await Assert.ThrowsAsync<UnauthorizedException>(() => Create().HandleAsync(new CreateOrganizationRequest("Acme")));
    }

    [Fact]
    public async Task List_returns_only_organizations_the_user_belongs_to()
    {
        await Create().HandleAsync(new CreateOrganizationRequest("Mine"));
        var other = new FakeCurrentUser(Guid.NewGuid());
        await new CreateOrganizationHandler(_orgs, _uow, new OrganizationAccess(_orgs, other), new FixedClock())
            .HandleAsync(new CreateOrganizationRequest("Theirs"));

        var mine = await new ListMyOrganizationsHandler(_orgs, Access()).HandleAsync();

        Assert.Equal("Mine", Assert.Single(mine).Name);
    }

    [Fact]
    public async Task Owner_can_add_member_by_email()
    {
        var org = await Create().HandleAsync(new CreateOrganizationRequest("Acme"));
        var friend = Guid.NewGuid();
        _identity.UsersByEmail["friend@example.com"] = friend;

        await AddMember().HandleAsync(org.Id, new AddMemberRequest("FRIEND@example.com", OrganizationRole.Viewer));

        Assert.Contains(_orgs.Members, m => m.UserId == friend && m.Role == OrganizationRole.Viewer);
    }

    [Fact]
    public async Task Editor_cannot_add_members()
    {
        var org = await Create().HandleAsync(new CreateOrganizationRequest("Acme"));
        var editor = Guid.NewGuid();
        _orgs.Members.Add(OrganizationMember.Create(org.Id, editor, OrganizationRole.Editor, DateTime.UtcNow));
        _user.UserId = editor;
        _identity.UsersByEmail["x@example.com"] = Guid.NewGuid();

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            AddMember().HandleAsync(org.Id, new AddMemberRequest("x@example.com", OrganizationRole.Viewer)));
    }

    [Fact]
    public async Task Adding_an_existing_member_is_a_conflict()
    {
        var org = await Create().HandleAsync(new CreateOrganizationRequest("Acme"));
        _identity.UsersByEmail["me@example.com"] = _user.UserId!.Value;

        await Assert.ThrowsAsync<ConflictException>(() =>
            AddMember().HandleAsync(org.Id, new AddMemberRequest("me@example.com", OrganizationRole.Editor)));
    }

    [Fact]
    public async Task Adding_an_unknown_email_is_not_found()
    {
        var org = await Create().HandleAsync(new CreateOrganizationRequest("Acme"));

        await Assert.ThrowsAsync<NotFoundException>(() =>
            AddMember().HandleAsync(org.Id, new AddMemberRequest("nobody@example.com", OrganizationRole.Viewer)));
    }

    [Fact]
    public async Task Invalid_role_is_a_validation_error()
    {
        var org = await Create().HandleAsync(new CreateOrganizationRequest("Acme"));
        _identity.UsersByEmail["x@example.com"] = Guid.NewGuid();

        await Assert.ThrowsAsync<ValidationException>(() =>
            AddMember().HandleAsync(org.Id, new AddMemberRequest("x@example.com", (OrganizationRole)99)));
    }
}
