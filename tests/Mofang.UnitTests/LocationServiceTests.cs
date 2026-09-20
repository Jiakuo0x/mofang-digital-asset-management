using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mofang.Application;
using Mofang.Domain;
using Mofang.Infrastructure.Identity;
using Mofang.Infrastructure.Persistence;
using Mofang.Infrastructure.Services;

namespace Mofang.UnitTests;

public sealed class LocationServiceTests
{
    [Fact]
    public async Task CodesFollowRenamesAndMovesForBothKinds()
    {
        await using var test = await Fixture.CreateAsync();
        var folderCode = (await test.Service().GetAsync("folder", test.Visible.Id, test.Admin, default)).Code;
        var assetCode = (await test.Service().GetAsync("asset", test.Asset.Id, test.Admin, default)).Code;
        test.Visible.ParentId = test.Hidden.Id;
        test.Visible.Name = "新目录";
        test.Asset.FileName = "新名字.txt";
        await test.Db.SaveChangesAsync();
        var folder = await test.Service().ResolveAsync(folderCode, test.Admin, default);
        var asset = await test.Service().ResolveAsync($"位置：/旧路径/旧文件.txt\n定位码：{assetCode}", test.Admin, default);
        Assert.Equal("/保密/新目录", Assert.Single(folder.Items).Path);
        Assert.Equal("/保密/新目录/新名字.txt", Assert.Single(asset.Items).Path);
        Assert.Equal(folderCode, folder.Items[0].Code);
        Assert.Equal(assetCode, asset.Items[0].Code);
    }

    [Fact]
    public async Task MovingAnAssetOutOfGrantedDirectoryRevokesLocationAccess()
    {
        await using var test = await Fixture.CreateAsync();
        var code = (await test.Service().GetAsync("asset", test.Asset.Id, test.Reader, default)).Code;
        test.Asset.FolderId = test.Hidden.Id;
        await test.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service().ResolveAsync(code, test.Reader, default));
        Assert.DoesNotContain("保密", error.Message);
    }

    [Fact]
    public async Task ViewOnlyUsersCanCopyAndLocateBothKinds()
    {
        await using var test = await Fixture.CreateAsync();
        foreach (var (kind, id) in new[] { ("folder", test.Visible.Id), ("asset", test.Asset.Id) })
        {
            var location = await test.Service().GetAsync(kind, id, test.Reader, default);
            var result = await test.Service().ResolveAsync(location.Code, test.Reader, default);
            Assert.Equal("exact", result.Match);
            Assert.Equal(id, Assert.Single(result.Items).Id);
        }
    }

    [Fact]
    public async Task AnotherLibraryNeverFallsBackToAnExistingLocalPath()
    {
        await using var test = await Fixture.CreateAsync();
        var other = Guid.NewGuid();
        var result = await test.Service().ResolveAsync($"位置：/共享/报告.txt\n定位码：MF1:{other}:asset:{test.Asset.Id}", test.Admin, default);
        Assert.Equal("differentLibrary", result.Match);
        Assert.Equal(other, result.ExpectedLibraryId);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ExactPathsAndAmbiguousNamesRespectPermissions()
    {
        await using var test = await Fixture.CreateAsync();
        var secret = Fixture.NewAsset(test.Hidden.Id, "报告.txt");
        test.Db.Assets.Add(secret);
        var sameNameFolder = Fixture.NewFolder("报告.txt", test.Visible.Id);
        test.Db.Folders.Add(sameNameFolder);
        await test.Db.SaveChangesAsync();
        var exact = await test.Service().ResolveAsync("/共享/报告.txt", test.Reader, default);
        Assert.Equal("candidates", exact.Match);
        Assert.Equal(2, exact.Items.Count);
        Assert.Contains(exact.Items, x => x.Kind == "folder");
        Assert.Contains(exact.Items, x => x.Kind == "asset");
        var search = await test.Service().ResolveAsync("报告", test.Reader, default);
        Assert.All(search.Items, x => Assert.StartsWith("/共享/", x.Path));
        Assert.DoesNotContain(search.Items, x => x.Id == secret.Id);
        var hidden = await test.Service().ResolveAsync("/保密/报告.txt", test.Reader, default);
        Assert.Empty(hidden.Items);
        Assert.Equal("none", hidden.Match);
    }

    [Fact]
    public async Task RootIsAStableFolderLocationAndRequiresRootPermission()
    {
        await using var test = await Fixture.CreateAsync();
        var root = await test.Service().GetAsync("folder", null, test.Admin, default);
        Assert.EndsWith(":folder:root", root.Code);
        Assert.Equal("/", root.Path);
        Assert.Null(Assert.Single((await test.Service().ResolveAsync("/", test.Admin, default)).Items).Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service().ResolveAsync(root.Code, test.Reader, default));
    }

    [Fact]
    public async Task DeletedTargetsAreExplicitAndPermanentlyDeletedTargetsUnavailable()
    {
        await using var test = await Fixture.CreateAsync();
        var assetCode = (await test.Service().GetAsync("asset", test.Asset.Id, test.Reader, default)).Code;
        var folderCode = (await test.Service().GetAsync("folder", test.Visible.Id, test.Reader, default)).Code;
        test.Asset.Status = EntityStatus.Deleted;
        test.Visible.Status = EntityStatus.Deleted;
        await test.Db.SaveChangesAsync();
        foreach (var code in new[] { assetCode, folderCode })
            Assert.Equal("Deleted", Assert.Single((await test.Service().ResolveAsync(code, test.Reader, default)).Items).Status);
        Assert.Empty((await test.Service().ResolveAsync("报告", test.Reader, default)).Items);
        test.Db.Assets.Remove(test.Asset);
        await test.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service().ResolveAsync(assetCode, test.Reader, default));
    }

    [Fact]
    public async Task NavigationOnlyAncestorsCannotBeLocatedAsViewableFolders()
    {
        await using var test = await Fixture.CreateAsync();
        test.Visible.ParentId = test.Hidden.Id;
        await test.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service().GetAsync("folder", test.Hidden.Id, test.Reader, default));
        Assert.Equal("/保密/共享/报告.txt", (await test.Service().GetAsync("asset", test.Asset.Id, test.Reader, default)).Path);
    }

    [Fact]
    public async Task DirectLocationWorksBeyondListLimitAndCandidatesAreBounded()
    {
        await using var test = await Fixture.CreateAsync();
        test.Db.Assets.AddRange(Enumerable.Range(0, 205).Select(i => Fixture.NewAsset(test.Visible.Id, $"报告-{i:D3}.txt")));
        await test.Db.SaveChangesAsync();
        var result = await test.Service().ResolveAsync("报告", test.Reader, default);
        Assert.Equal(50, result.Items.Count);
        Assert.True(result.HasMore);
        var exact = await test.Service().ResolveAsync("/共享/报告.txt", test.Reader, default);
        Assert.Equal(test.Asset.Id, Assert.Single(exact.Items).Id);
    }

    [Fact]
    public async Task UnicodeSpacesAndLiteralPercentInPathsArePreserved()
    {
        await using var test = await Fixture.CreateAsync();
        test.Asset.FileName = "中文 100% #成片.txt";
        await test.Db.SaveChangesAsync();
        var result = await test.Service().ResolveAsync("\"\\共享\\中文 100% #成片.txt\"", test.Reader, default);
        Assert.Equal("exact", result.Match);
        Assert.Equal(test.Asset.Id, Assert.Single(result.Items).Id);
    }

    [Theory]
    [InlineData("MF1:broken:asset:id")]
    [InlineData("位置：/共享/报告.txt\n定位码：bad")]
    [InlineData("/共享/../保密")]
    [InlineData("C:\\Users\\file.txt")]
    [InlineData("https://example.com/download")]
    [InlineData("one\ntwo")]
    [InlineData(" ")]
    public void InvalidInputDoesNotFallBackToNames(string input) => Assert.Throws<ArgumentException>(() => LocationInput.Parse(input));

    [Fact]
    public void UnsupportedVersionsAndMultipleCodesAreRejected()
    {
        var code = $"MF1:{Guid.NewGuid()}:folder:{Guid.NewGuid()}";
        Assert.Throws<ArgumentException>(() => LocationInput.Parse(code.Replace("MF1:", "MF2:")));
        Assert.Throws<ArgumentException>(() => LocationInput.Parse(code + "\n" + code));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public MofangDbContext Db { get; }
        public Folder Visible { get; } = NewFolder("共享");
        public Folder Hidden { get; } = NewFolder("保密");
        public Asset Asset { get; }
        public AccountContext Admin { get; } = new(Guid.NewGuid(), "admin", "管理员", true);
        public AccountContext Reader { get; } = new(Guid.NewGuid(), "reader", "只读用户", false);

        private Fixture(SqliteConnection connection, MofangDbContext db)
        {
            this.connection = connection;
            Db = db;
            Asset = NewAsset(Visible.Id, "报告.txt");
        }

        public LocationService Service() => new(Db, new DirectoryAccessService(Db));

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MofangDbContext(new DbContextOptionsBuilder<MofangDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, db);
            db.LibraryIdentities.Add(new LibraryIdentity { Id = 1, LibraryId = Guid.NewGuid() });
            db.Folders.AddRange(fixture.Visible, fixture.Hidden);
            db.Assets.Add(fixture.Asset);
            db.Users.Add(new ApplicationUser { Id = fixture.Reader.Id, UserName = "reader", DisplayName = "只读用户" });
            db.DirectoryPermissions.Add(new DirectoryPermission { Id = Guid.NewGuid(), AccountId = fixture.Reader.Id, FolderId = fixture.Visible.Id, CanView = true });
            await db.SaveChangesAsync();
            return fixture;
        }

        public static Folder NewFolder(string name, Guid? parentId = null) => new() { Id = Guid.NewGuid(), Name = name, ParentId = parentId, CreatedBy = "admin" };
        public static Asset NewAsset(Guid folderId, string name) => new()
        {
            Id = Guid.NewGuid(), FolderId = folderId, FileName = name, OriginalFileName = name,
            Extension = ".txt", MimeType = "text/plain", Bucket = "test", ObjectKey = Guid.NewGuid().ToString(), Hash = "test", CreatedBy = "admin"
        };
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
