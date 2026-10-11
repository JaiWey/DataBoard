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

    // ---- managing members ----------------------------------------------------------------------------------------

    private ListMembersHandler List() => new(_orgs, _identity, Access());
    private ChangeMemberRoleHandler ChangeRole() => new(_orgs, _uow, Access());
    private RemoveMemberHandler Remove() => new(_orgs, _uow, Access());

    private Guid Join(Guid orgId, OrganizationRole role, string name, string? email = null)
    {
        var id = Guid.NewGuid();
        _orgs.Members.Add(OrganizationMember.Create(orgId, id, role, DateTime.UtcNow));
        _identity.People.Add(new Identity.UserInfo(id, email ?? $"{name.ToLowerInvariant()}@example.com", name));
        return id;
    }

    private async Task<(Guid Org, Guid Me)> OwnedOrgAsync()
    {
        var org = await Create().HandleAsync(new CreateOrganizationRequest("Acme"));
        _identity.People.Add(new Identity.UserInfo(_user.UserId!.Value, "me@example.com", "Me"));
        return (org.Id, _user.UserId!.Value);
    }

    [Fact]
    public async Task Any_member_sees_everyone_with_owners_first_then_by_name()
    {
        var (org, _) = await OwnedOrgAsync();
        Join(org, OrganizationRole.Viewer, "Zed");
        Join(org, OrganizationRole.Editor, "Bea");
        Join(org, OrganizationRole.Viewer, "Amy");
        Join(Guid.NewGuid(), OrganizationRole.Owner, "Elsewhere"); // another organization
        _user.UserId = _orgs.Members.Single(m => m.OrganizationId == org && m.Role == OrganizationRole.Editor).UserId;

        var members = await List().HandleAsync(org);

        Assert.Equal(["Me", "Bea", "Amy", "Zed"], members.Select(m => m.DisplayName));
        Assert.Equal(OrganizationRole.Owner, members[0].Role);
        Assert.Equal("me@example.com", members[0].Email);
    }

    [Fact]
    public async Task A_stranger_cannot_list_members()
    {
        var (org, _) = await OwnedOrgAsync();
        _user.UserId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => List().HandleAsync(org));
    }

    [Fact]
    public async Task A_member_with_no_account_any_more_is_left_out_of_the_list()
    {
        var (org, _) = await OwnedOrgAsync();
        _orgs.Members.Add(OrganizationMember.Create(org, Guid.NewGuid(), OrganizationRole.Viewer, DateTime.UtcNow));

        Assert.Equal("Me", Assert.Single(await List().HandleAsync(org)).DisplayName);
    }

    [Fact]
    public async Task An_owner_changes_a_role_both_ways()
    {
        var (org, _) = await OwnedOrgAsync();
        var bea = Join(org, OrganizationRole.Viewer, "Bea");

        await ChangeRole().HandleAsync(org, bea, new ChangeMemberRoleRequest(OrganizationRole.Editor));
        Assert.Equal(OrganizationRole.Editor, _orgs.Members.Single(m => m.UserId == bea).Role);

        await ChangeRole().HandleAsync(org, bea, new ChangeMemberRoleRequest(OrganizationRole.Viewer));
        Assert.Equal(OrganizationRole.Viewer, _orgs.Members.Single(m => m.UserId == bea).Role);
        Assert.Equal(2, _uow.Saves - 1); // one save came from creating the organization
    }

    [Fact]
    public async Task Saying_the_role_they_already_have_changes_and_saves_nothing()
    {
        var (org, _) = await OwnedOrgAsync();
        var bea = Join(org, OrganizationRole.Editor, "Bea");
        var saves = _uow.Saves;

        await ChangeRole().HandleAsync(org, bea, new ChangeMemberRoleRequest(OrganizationRole.Editor));

        Assert.Equal(saves, _uow.Saves);
    }

    [Theory]
    [InlineData(OrganizationRole.Editor)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task Only_an_owner_changes_roles(OrganizationRole role)
    {
        var (org, _) = await OwnedOrgAsync();
        var bea = Join(org, OrganizationRole.Viewer, "Bea");
        _user.UserId = Join(org, role, "Caller");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            ChangeRole().HandleAsync(org, bea, new ChangeMemberRoleRequest(OrganizationRole.Owner)));
        Assert.Equal(OrganizationRole.Viewer, _orgs.Members.Single(m => m.UserId == bea).Role);
    }

    [Fact]
    public async Task An_unknown_role_or_a_person_who_is_not_a_member_is_refused()
    {
        var (org, _) = await OwnedOrgAsync();
        var bea = Join(org, OrganizationRole.Viewer, "Bea");

        await Assert.ThrowsAsync<ValidationException>(() => ChangeRole().HandleAsync(org, bea, new ChangeMemberRoleRequest((OrganizationRole)0)));
        await Assert.ThrowsAsync<NotFoundException>(() => ChangeRole().HandleAsync(org, Guid.NewGuid(), new ChangeMemberRoleRequest(OrganizationRole.Editor)));
    }

    [Fact]
    public async Task The_last_owner_cannot_be_demoted_even_by_themselves()
    {
        var (org, me) = await OwnedOrgAsync();
        Join(org, OrganizationRole.Editor, "Bea");

        var failure = await Assert.ThrowsAsync<ConflictException>(() =>
            ChangeRole().HandleAsync(org, me, new ChangeMemberRoleRequest(OrganizationRole.Editor)));

        Assert.Equal(MemberRules.LastOwner, failure.Message);
        Assert.Equal(OrganizationRole.Owner, _orgs.Members.Single(m => m.UserId == me).Role);
    }

    [Fact]
    public async Task An_owner_can_step_down_once_there_is_another_owner()
    {
        var (org, me) = await OwnedOrgAsync();
        var bea = Join(org, OrganizationRole.Editor, "Bea");

        await ChangeRole().HandleAsync(org, bea, new ChangeMemberRoleRequest(OrganizationRole.Owner));
        await ChangeRole().HandleAsync(org, me, new ChangeMemberRoleRequest(OrganizationRole.Viewer));

        Assert.Equal(OrganizationRole.Viewer, _orgs.Members.Single(m => m.UserId == me).Role);
        Assert.Single(_orgs.Members, m => m.Role == OrganizationRole.Owner);
    }

    [Fact]
    public async Task An_owner_removes_a_member_and_the_member_can_be_added_again()
    {
        var (org, _) = await OwnedOrgAsync();
        var bea = Join(org, OrganizationRole.Editor, "Bea");
        _identity.UsersByEmail["bea@example.com"] = bea;

        await Remove().HandleAsync(org, bea);
        Assert.DoesNotContain(_orgs.Members, m => m.UserId == bea);

        await AddMember().HandleAsync(org, new AddMemberRequest("bea@example.com", OrganizationRole.Viewer));
        Assert.Contains(_orgs.Members, m => m.UserId == bea && m.Role == OrganizationRole.Viewer);
    }

    [Theory]
    [InlineData(OrganizationRole.Editor)]
    [InlineData(OrganizationRole.Viewer)]
    public async Task Only_an_owner_removes_somebody_else(OrganizationRole role)
    {
        var (org, me) = await OwnedOrgAsync();
        _user.UserId = Join(org, role, "Caller");

        await Assert.ThrowsAsync<ForbiddenException>(() => Remove().HandleAsync(org, me));
        Assert.Contains(_orgs.Members, m => m.UserId == me);
    }

    [Theory]
    [InlineData(OrganizationRole.Viewer)]
    [InlineData(OrganizationRole.Editor)]
    public async Task Anyone_can_leave(OrganizationRole role)
    {
        var (org, _) = await OwnedOrgAsync();
        var leaver = Join(org, role, "Leaver");
        _user.UserId = leaver;

        await Remove().HandleAsync(org, leaver);

        Assert.DoesNotContain(_orgs.Members, m => m.UserId == leaver);
    }

    [Fact]
    public async Task The_last_owner_can_neither_be_removed_nor_leave()
    {
        var (org, me) = await OwnedOrgAsync();
        Join(org, OrganizationRole.Editor, "Bea");

        var failure = await Assert.ThrowsAsync<ConflictException>(() => Remove().HandleAsync(org, me));

        Assert.Equal(MemberRules.LastOwner, failure.Message);
        Assert.Contains(_orgs.Members, m => m.UserId == me);
    }

    [Fact]
    public async Task Nobody_can_leave_or_be_removed_from_the_default_organization()
    {
        var org = Organization.CreateDefault(DateTime.UtcNow);
        _orgs.Organizations.Add(org);
        var me = _user.UserId!.Value;
        _orgs.Members.Add(OrganizationMember.Create(org.Id, me, OrganizationRole.Owner, DateTime.UtcNow));
        var bea = Join(org.Id, OrganizationRole.Editor, "Bea");

        var leave = await Assert.ThrowsAsync<ConflictException>(() => Remove().HandleAsync(org.Id, me));
        var remove = await Assert.ThrowsAsync<ConflictException>(() => Remove().HandleAsync(org.Id, bea));

        Assert.Equal(MemberRules.DefaultOrganization, leave.Message);
        Assert.Equal(MemberRules.DefaultOrganization, remove.Message);
        Assert.Equal(2, _orgs.Members.Count);
        var listed = Assert.Single(await new ListMyOrganizationsHandler(_orgs, Access()).HandleAsync());
        Assert.True(listed.IsDefault);
    }

    [Fact]
    public async Task Removing_one_of_two_owners_is_fine_and_removing_a_stranger_is_not_found()
    {
        var (org, _) = await OwnedOrgAsync();
        var second = Join(org, OrganizationRole.Owner, "Second");

        await Remove().HandleAsync(org, second);
        await Assert.ThrowsAsync<NotFoundException>(() => Remove().HandleAsync(org, Guid.NewGuid()));
        Assert.Single(_orgs.Members);
    }

    [Fact]
    public async Task Someone_outside_the_organization_cannot_remove_anyone_or_even_leave_it()
    {
        var (org, me) = await OwnedOrgAsync();
        _user.UserId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => Remove().HandleAsync(org, me));
        await Assert.ThrowsAsync<NotFoundException>(() => Remove().HandleAsync(org, _user.UserId.Value));
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
