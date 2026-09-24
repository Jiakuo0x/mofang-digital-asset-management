using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mofang.Application;
using Mofang.Domain;
using Mofang.Infrastructure.Identity;
using Mofang.Infrastructure.Persistence;
using Mofang.Infrastructure.Services;

namespace Mofang.UnitTests;

public sealed class DamServiceSearchAssetsTests
{
    [Fact]
    public async Task LibraryRootShowsOnlyDirectAssetsWhileTrashRemainsAggregated()
    {
        await using var test = await TestContext.CreateAsync();
        var folder = NewFolder("项目");
        var child = NewFolder("成片", folder.Id);
        var rootAsset = NewAsset(null, "根目录.txt");
        var folderAsset = NewAsset(folder.Id, "项目.txt");
        var childAsset = NewAsset(child.Id, "成片.txt");
        var deletedRootAsset = NewAsset(null, "已删除根目录.txt", EntityStatus.Deleted);
        var deletedChildAsset = NewAsset(child.Id, "已删除成片.txt", EntityStatus.Deleted);
        test.Db.AddRange(folder, child, rootAsset, folderAsset, childAsset, deletedRootAsset, deletedChildAsset);
        await test.Db.SaveChangesAsync();

        var root = await test.SearchAsync(null);
        Assert.Equal(1, root.Total);
        Assert.Equal(rootAsset.Id, Assert.Single(root.Items).Id);

        var project = await test.SearchAsync(folder.Id);
        Assert.Equal(1, project.Total);
        Assert.Equal(folderAsset.Id, Assert.Single(project.Items).Id);

        var trash = await test.SearchAsync(null, trash: true);
        Assert.Equal(2, trash.Total);
        Assert.Contains(trash.Items, x => x.Id == deletedRootAsset.Id);
        Assert.Contains(trash.Items, x => x.Id == deletedChildAsset.Id);
    }

    [Fact]
    public async Task AccountWithoutRootAccessDoesNotSeeRootAssets()
    {
        await using var test = await TestContext.CreateAsync();
        var folder = NewFolder("授权目录");
        var rootAsset = NewAsset(null, "根目录.txt");
        var folderAsset = NewAsset(folder.Id, "授权文件.txt");
        var reader = new ApplicationUser { Id = Guid.NewGuid(), UserName = "reader", NormalizedUserName = "READER", DisplayName = "读者", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        test.Db.AddRange(folder, rootAsset, folderAsset, reader);
        test.Db.DirectoryPermissions.Add(new DirectoryPermission { Id = Guid.NewGuid(), AccountId = reader.Id, FolderId = folder.Id, CanView = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await test.Db.SaveChangesAsync();

        var account = new AccountContext(reader.Id, "reader", "读者", false);
        Assert.Empty((await test.SearchAsync(null, account: account)).Items);
        Assert.Equal(folderAsset.Id, Assert.Single((await test.SearchAsync(folder.Id, account: account)).Items).Id);
    }

    private static Folder NewFolder(string name, Guid? parentId = null) => new()
    {
        Id = Guid.NewGuid(), ParentId = parentId, Name = name, CreatedBy = "admin", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private static Asset NewAsset(Guid? folderId, string name, EntityStatus status = EntityStatus.Active) => new()
    {
        Id = Guid.NewGuid(), FolderId = folderId, FileName = name, OriginalFileName = name, Extension = ".txt", MimeType = "text/plain",
        AssetType = AssetType.Document, FileSize = 1, Bucket = "assets", ObjectKey = Guid.NewGuid().ToString("N"), Hash = "test",
        Status = status, CreatedBy = "admin", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class TestContext(SqliteConnection connection, MofangDbContext db) : IAsyncDisposable
    {
        public MofangDbContext Db { get; } = db;
        private readonly DamService _service = new(db, new PreviewStorage(), null!, new DirectoryAccessService(db), null!);
        private readonly AccountContext _admin = new(Guid.NewGuid(), "admin", "管理员", true);

        public static async Task<TestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MofangDbContext(new DbContextOptionsBuilder<MofangDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new TestContext(connection, db);
        }

        public Task<Mofang.Contracts.AssetPageDto> SearchAsync(Guid? folderId, bool trash = false, AccountContext? account = null) =>
            _service.SearchAssetsAsync(folderId, null, null, trash, null, null, null, null, "name", 1, 100, account ?? _admin, CancellationToken.None);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class PreviewStorage : IAssetStorage
    {
        public Task<string> GetPreviewUrlAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => Task.FromResult("https://example.test/preview");
        public Task EnsureReadyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageWriteResult> UploadAsync(string bucket, string objectKey, Stream content, long size, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DownloadToAsync(string bucket, string objectKey, Stream destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CopyAsync(string sourceBucket, string sourceObjectKey, string destinationBucket, string destinationObjectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageObjectMetadata> GetMetadataAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetDownloadUrlAsync(string bucket, string objectKey, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
