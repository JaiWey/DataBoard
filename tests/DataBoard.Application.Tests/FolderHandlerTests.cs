using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Tests;

public class FolderHandlerTests
{
    private readonly FakeFolderRepo _folders = new();
    private readonly FakeDatasetRepo _datasets = new();
    private readonly FakeOrganizationRepo _orgs = new();
    private readonly FakeUow _uow = new();
    private readonly FakeCurrentUser _user = new(Guid.NewGuid());
    private readonly Guid _org = Guid.NewGuid();

    public FolderHandlerTests() => _folders.Datasets = _datasets;

    private OrganizationAccess Access() => new(_orgs, _user);
    private void Join(OrganizationRole role, Guid? org = null) =>
        _orgs.Members.Add(OrganizationMember.Create(org ?? _org, _user.UserId!.Value, role, DateTime.UtcNow));

    private CreateFolderHandler Create() => new(_folders, _uow, new FixedClock(), Access());
    private UpdateFolderHandler Update() => new(_folders, _uow, new FixedClock(), Access());
    private DeleteFolderHandler Delete() => new(_folders, _uow, Access());
    private MoveDatasetHandler MoveDataset() => new(_datasets, _folders, _uow, Access());

    private async Task<DatasetFolderDto> Folder(string name, Guid? parent = null) =>
        await Create().HandleAsync(_org, new CreateFolderRequest(name, parent));

    private Dataset Dataset(string name = "Orders", Guid? folder = null)
    {
        var dataset = Domain.Datasets.Dataset.Create(
            _org, Guid.NewGuid(), name, null, "SELECT 1", [new DatasetColumn("n", "integer", ColumnRole.Measure)],
            Guid.NewGuid(), DateTime.UtcNow, folderId: folder);
        _datasets.Items.Add(dataset);
        return dataset;
    }

    // ---- create --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Editor_creates_folders_at_the_top_and_inside_other_folders()
    {
        Join(OrganizationRole.Editor);

        var sales = await Folder("  Sales ");
        var europe = await Folder("Europe", sales.Id);

        Assert.Equal("Sales", sales.Name);
        Assert.Null(sales.ParentId);
        Assert.Equal(sales.Id, europe.ParentId);
        Assert.Equal(2, _folders.Items.Count);
    }

    [Fact]
    public async Task Viewer_can_list_but_not_change_and_non_member_gets_not_found()
    {
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<ForbiddenException>(() => Folder("Sales"));
        Assert.Empty(await new ListFoldersHandler(_folders, Access()).HandleAsync(_org));

        _user.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => Folder("Sales"));
        Assert.Empty(_folders.Items);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public async Task A_bad_name_is_a_validation_error(string name)
    {
        Join(OrganizationRole.Editor);

        await Assert.ThrowsAsync<ValidationException>(() => Folder(name));
    }

    [Fact]
    public async Task Sibling_names_are_unique_ignoring_case_but_the_same_name_can_live_elsewhere()
    {
        Join(OrganizationRole.Editor);
        var sales = await Folder("Sales");
        await Folder("Europe", sales.Id);

        await Assert.ThrowsAsync<ConflictException>(() => Folder("sales"));
        await Assert.ThrowsAsync<ConflictException>(() => Folder("EUROPE", sales.Id));

        await Folder("Europe"); // top level, not a sibling
        Assert.Equal(3, _folders.Items.Count);
    }

    [Fact]
    public async Task The_parent_must_exist_in_the_organization()
    {
        Join(OrganizationRole.Editor);

        await Assert.ThrowsAsync<ValidationException>(() => Folder("Orphan", Guid.NewGuid()));

        var other = DatasetFolder.Create(Guid.NewGuid(), null, "Elsewhere", Guid.NewGuid(), DateTime.UtcNow);
        _folders.Items.Add(other);
        await Assert.ThrowsAsync<ValidationException>(() => Folder("Orphan", other.Id));
    }

    [Fact]
    public async Task Folders_nest_up_to_the_depth_limit_and_no_further()
    {
        Join(OrganizationRole.Editor);
        Guid? parent = null;
        for (var level = 1; level <= DatasetFolder.MaxDepth; level++)
            parent = (await Folder($"Level {level}", parent)).Id;

        await Assert.ThrowsAsync<ValidationException>(() => Folder("Too deep", parent));
    }

    // ---- rename and move -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_folder_is_renamed_and_moved_with_its_contents()
    {
        Join(OrganizationRole.Editor);
        var sales = await Folder("Sales");
        var archive = await Folder("Archive");
        var europe = await Folder("Europe", sales.Id);
        var inEurope = Dataset(folder: europe.Id);

        var moved = await Update().HandleAsync(_org, europe.Id, new UpdateFolderRequest("EMEA", archive.Id));

        Assert.Equal("EMEA", moved.Name);
        Assert.Equal(archive.Id, moved.ParentId);
        Assert.Equal(europe.Id, inEurope.FolderId); // the dataset stays in the folder, which moved
        var toTop = await Update().HandleAsync(_org, europe.Id, new UpdateFolderRequest("EMEA", null));
        Assert.Null(toTop.ParentId);
    }

    [Fact]
    public async Task A_folder_cannot_go_into_itself_or_a_folder_below_it()
    {
        Join(OrganizationRole.Editor);
        var a = await Folder("A");
        var b = await Folder("B", a.Id);
        var c = await Folder("C", b.Id);

        await Assert.ThrowsAsync<ValidationException>(() => Update().HandleAsync(_org, a.Id, new UpdateFolderRequest("A", a.Id)));
        await Assert.ThrowsAsync<ValidationException>(() => Update().HandleAsync(_org, a.Id, new UpdateFolderRequest("A", c.Id)));

        Assert.Null(_folders.Items.Single(f => f.Id == a.Id).ParentId);
    }

    [Fact]
    public async Task A_move_that_would_make_the_tree_too_deep_is_refused_counting_the_whole_subtree()
    {
        Join(OrganizationRole.Editor);
        Guid? parent = null;
        Guid? deepest = null;
        for (var level = 1; level <= DatasetFolder.MaxDepth - 1; level++)
            deepest = parent = (await Folder($"Level {level}", parent)).Id;
        var small = await Folder("Small");
        var smallChild = await Folder("Small child", small.Id);

        // Small (height 2) under level 7 would reach level 9; a single folder under level 7 reaches level 8.
        await Assert.ThrowsAsync<ValidationException>(() => Update().HandleAsync(_org, small.Id, new UpdateFolderRequest("Small", deepest)));
        await Update().HandleAsync(_org, smallChild.Id, new UpdateFolderRequest("Small child", deepest));
    }

    [Fact]
    public async Task Renaming_onto_a_sibling_is_a_conflict_and_keeping_the_name_is_fine()
    {
        Join(OrganizationRole.Editor);
        var sales = await Folder("Sales");
        await Folder("Ops");

        await Assert.ThrowsAsync<ConflictException>(() => Update().HandleAsync(_org, sales.Id, new UpdateFolderRequest("ops", null)));
        var same = await Update().HandleAsync(_org, sales.Id, new UpdateFolderRequest("Sales", null));

        Assert.Equal("Sales", same.Name);
    }

    [Fact]
    public async Task Viewers_cannot_change_folders_and_unknown_ones_are_not_found()
    {
        Join(OrganizationRole.Editor);
        var sales = await Folder("Sales");
        _orgs.Members.Clear();
        Join(OrganizationRole.Viewer);

        await Assert.ThrowsAsync<ForbiddenException>(() => Update().HandleAsync(_org, sales.Id, new UpdateFolderRequest("X", null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => Delete().HandleAsync(_org, sales.Id));

        _orgs.Members.Clear();
        Join(OrganizationRole.Editor);
        await Assert.ThrowsAsync<NotFoundException>(() => Update().HandleAsync(_org, Guid.NewGuid(), new UpdateFolderRequest("X", null)));
    }

    // ---- delete --------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_empty_folder_is_deleted()
    {
        Join(OrganizationRole.Editor);
        var sales = await Folder("Sales");

        await Delete().HandleAsync(_org, sales.Id);

        Assert.Empty(_folders.Items);
    }

    [Fact]
    public async Task A_folder_with_a_subfolder_or_a_dataset_is_not_deleted()
    {
        Join(OrganizationRole.Editor);
        var sales = await Folder("Sales");
        var europe = await Folder("Europe", sales.Id);
        Dataset(folder: europe.Id);

        await Assert.ThrowsAsync<ConflictException>(() => Delete().HandleAsync(_org, sales.Id));
        await Assert.ThrowsAsync<ConflictException>(() => Delete().HandleAsync(_org, europe.Id));

        Assert.Equal(2, _folders.Items.Count);
    }

    // ---- filing datasets -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_dataset_is_filed_in_a_folder_and_taken_out_again_without_counting_as_an_edit()
    {
        Join(OrganizationRole.Editor);
        var sales = await Folder("Sales");
        var dataset = Dataset();
        var updated = dataset.UpdatedUtc;

        var filed = await MoveDataset().HandleAsync(_org, dataset.Id, new MoveDatasetRequest(sales.Id));
        Assert.Equal(sales.Id, filed.FolderId);
        Assert.Equal(sales.Id, dataset.FolderId);

        var top = await MoveDataset().HandleAsync(_org, dataset.Id, new MoveDatasetRequest(null));
        Assert.Null(top.FolderId);
        Assert.Equal(updated, dataset.UpdatedUtc);
    }

    [Fact]
    public async Task A_dataset_cannot_go_into_a_missing_folder_and_viewers_cannot_file_datasets()
    {
        Join(OrganizationRole.Editor);
        var dataset = Dataset();

        await Assert.ThrowsAsync<ValidationException>(() => MoveDataset().HandleAsync(_org, dataset.Id, new MoveDatasetRequest(Guid.NewGuid())));
        await Assert.ThrowsAsync<NotFoundException>(() => MoveDataset().HandleAsync(_org, Guid.NewGuid(), new MoveDatasetRequest(null)));

        _orgs.Members.Clear();
        Join(OrganizationRole.Viewer);
        await Assert.ThrowsAsync<ForbiddenException>(() => MoveDataset().HandleAsync(_org, dataset.Id, new MoveDatasetRequest(null)));
    }
}
