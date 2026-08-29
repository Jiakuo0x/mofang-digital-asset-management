using Mofang.Application;

namespace Mofang.UnitTests;

public sealed class DirectoryAccessEvaluatorTests
{
    private static readonly Guid RootFolder = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ChildFolder = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid GrandchildFolder = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OtherFolder = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly FolderAccessNode[] Folders =
    [
        new(RootFolder, null),
        new(ChildFolder, RootFolder),
        new(GrandchildFolder, ChildFolder),
        new(OtherFolder, null)
    ];

    [Fact]
    public void FolderGrant_InheritsToDescendants_AndKeepsAncestorsForNavigation()
    {
        var result = DirectoryAccessEvaluator.Evaluate(false, Folders, [new DirectoryGrant(ChildFolder, true, false)]);

        Assert.True(result.Folders[ChildFolder].CanView);
        Assert.True(result.Folders[GrandchildFolder].CanView);
        Assert.False(result.Folders[GrandchildFolder].CanOperate);
        Assert.True(result.Folders[RootFolder].NavigationOnly);
        Assert.False(result.Folders.ContainsKey(OtherFolder));
    }

    [Fact]
    public void OperateGrant_AlwaysIncludesView()
    {
        var result = DirectoryAccessEvaluator.Evaluate(false, Folders, [new DirectoryGrant(ChildFolder, false, true)]);

        Assert.True(result.Folders[ChildFolder].CanView);
        Assert.True(result.Folders[ChildFolder].CanOperate);
        Assert.True(result.Folders[GrandchildFolder].CanView);
        Assert.True(result.Folders[GrandchildFolder].CanOperate);
    }

    [Fact]
    public void RootGrant_AppliesToRootAndEveryFolder()
    {
        var result = DirectoryAccessEvaluator.Evaluate(false, Folders, [new DirectoryGrant(null, true, true)]);

        Assert.True(result.CanViewRoot);
        Assert.True(result.CanOperateRoot);
        Assert.All(result.Folders.Values, access =>
        {
            Assert.True(access.CanView);
            Assert.True(access.CanOperate);
            Assert.False(access.NavigationOnly);
        });
    }

    [Fact]
    public void MasterAdmin_HasUnrestrictedAccessWithoutGrants()
    {
        var result = DirectoryAccessEvaluator.Evaluate(true, Folders, []);

        Assert.True(result.CanViewRoot);
        Assert.True(result.CanOperateRoot);
        Assert.Equal(Folders.Length, result.Folders.Count);
        Assert.All(result.Folders.Values, access => Assert.True(access.CanOperate));
    }
}
