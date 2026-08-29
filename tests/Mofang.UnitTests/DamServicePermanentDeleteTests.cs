using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Mofang.Application;
using Mofang.Domain;
using Mofang.Infrastructure.Identity;
using Mofang.Infrastructure.Persistence;
using Mofang.Infrastructure.Services;

namespace Mofang.UnitTests;

public sealed class DamServicePermanentDeleteTests
{
    [Fact]
    public async Task PermanentlyDeleteAsset_RemovesMetadataOriginalAndThumbnail()
    {
        await using var test = await TestContext.CreateAsync();
        var asset = CreateDeletedAsset(null, "poster.png", includeThumbnail: true);
        test.Db.Assets.Add(asset);
        await test.Db.SaveChangesAsync();

        await test.Service.PermanentlyDeleteAssetAsync(asset.Id, test.Account, CancellationToken.None);

        Assert.Empty(await test.Db.Assets.ToListAsync());
        Assert.Empty(await test.Db.AssetVersions.ToListAsync());
        Assert.Empty(await test.Db.StorageObjects.ToListAsync());
        Assert.Contains((asset.Bucket, asset.ObjectKey), test.Storage.DeletedObjects);
        Assert.Contains((asset.ThumbnailBucket!, asset.ThumbnailObjectKey!), test.Storage.DeletedObjects);
        Assert.Equal("PermanentDelete", (await test.Db.OperationLogs.SingleAsync()).Action);
    }

    [Fact]
    public async Task PermanentlyDeleteFolder_RemovesDeletedTreeAssetsAndPermissions()
    {
        await using var test = await TestContext.CreateAsync();
        var root = CreateDeletedFolder("项目素材");
        var child = CreateDeletedFolder("成片", root.Id);
        var asset = CreateDeletedAsset(child.Id, "final.mov", includeThumbnail: false);
        test.Db.AddRange(root, child, asset);
        test.Db.DirectoryPermissions.Add(new DirectoryPermission
        {
            Id = Guid.NewGuid(),
            AccountId = test.Account.Id,
            FolderId = child.Id,
            CanView = true,
            CanOperate = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await test.Db.SaveChangesAsync();

        await test.Service.PermanentlyDeleteFolderAsync(root.Id, test.Account, CancellationToken.None);

        Assert.Empty(await test.Db.Folders.ToListAsync());
        Assert.Empty(await test.Db.Assets.ToListAsync());
        Assert.Empty(await test.Db.AssetVersions.ToListAsync());
        Assert.Empty(await test.Db.StorageObjects.ToListAsync());
        Assert.Empty(await test.Db.DirectoryPermissions.ToListAsync());
        Assert.Contains((asset.Bucket, asset.ObjectKey), test.Storage.DeletedObjects);
        var log = await test.Db.OperationLogs.SingleAsync();
        Assert.Equal("PermanentDelete", log.Action);
        Assert.Equal("Folder", log.TargetType);
    }

    [Fact]
    public async Task PermanentlyDeleteAsset_WhenStorageFails_KeepsMetadataForRetry()
    {
        await using var test = await TestContext.CreateAsync();
        var asset = CreateDeletedAsset(null, "retry.zip", includeThumbnail: false);
        test.Db.Assets.Add(asset);
        await test.Db.SaveChangesAsync();
        test.Storage.FailDeletes = true;

        await Assert.ThrowsAsync<IOException>(() => test.Service.PermanentlyDeleteAssetAsync(asset.Id, test.Account, CancellationToken.None));

        Assert.True(await test.Db.Assets.AnyAsync(x => x.Id == asset.Id));
        Assert.True(await test.Db.StorageObjects.AnyAsync());
        Assert.Empty(await test.Db.OperationLogs.ToListAsync());
    }

    [Fact]
    public async Task PermanentlyDeleteFolder_WithActiveAsset_RejectsWithoutDeletingStorage()
    {
        await using var test = await TestContext.CreateAsync();
        var folder = CreateDeletedFolder("异常目录");
        var asset = CreateDeletedAsset(folder.Id, "active.psd", includeThumbnail: false);
        asset.Status = EntityStatus.Active;
        test.Db.AddRange(folder, asset);
        await test.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => test.Service.PermanentlyDeleteFolderAsync(folder.Id, test.Account, CancellationToken.None));

        Assert.Contains("未删除的资产", exception.Message);
        Assert.Empty(test.Storage.DeletedObjects);
        Assert.True(await test.Db.Assets.AnyAsync(x => x.Id == asset.Id));
    }

    private static Folder CreateDeletedFolder(string name, Guid? parentId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new Folder
        {
            Id = Guid.NewGuid(),
            ParentId = parentId,
            Name = name,
            CreatedBy = "admin",
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = now,
            Status = EntityStatus.Deleted
        };
    }

    private static Asset CreateDeletedAsset(Guid? folderId, string fileName, bool includeThumbnail)
    {
        var now = DateTimeOffset.UtcNow;
        var assetId = Guid.NewGuid();
        var extension = Path.GetExtension(fileName);
        var storageObject = new StorageObject
        {
            Id = Guid.NewGuid(),
            Provider = "MinIO",
            Bucket = "assets",
            ObjectKey = $"assets/{assetId:N}{extension}",
            FileSize = 123,
            Hash = "hash",
            CreatedAt = now
        };
        var asset = new Asset
        {
            Id = assetId,
            FolderId = folderId,
            FileName = fileName,
            OriginalFileName = fileName,
            Extension = extension,
            MimeType = "application/octet-stream",
            FileSize = 123,
            AssetType = AssetType.Other,
            Bucket = storageObject.Bucket,
            ObjectKey = storageObject.ObjectKey,
            Hash = storageObject.Hash,
            Status = EntityStatus.Deleted,
            CreatedBy = "admin",
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = now,
            CurrentVersionNumber = 1,
            ThumbnailStatus = includeThumbnail ? ThumbnailStatus.Ready : ThumbnailStatus.NotRequired,
            ThumbnailBucket = includeThumbnail ? "thumbnails" : null,
            ThumbnailObjectKey = includeThumbnail ? $"thumbnails/{assetId:N}.webp" : null
        };
        asset.Versions.Add(new AssetVersion
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            StorageObjectId = storageObject.Id,
            StorageObject = storageObject,
            VersionNumber = 1,
            FileName = fileName,
            MimeType = asset.MimeType,
            FileSize = asset.FileSize,
            Hash = asset.Hash,
            CreatedBy = "admin",
            CreatedAt = now
        });
        return asset;
    }

    private sealed class TestContext : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestContext(SqliteConnection connection, MofangDbContext db, FakeAssetStorage storage, AccountContext account)
        {
            _connection = connection;
            Db = db;
            Storage = storage;
            Account = account;
            Service = new DamService(db, storage, new FakeThumbnailQueue(), new AllowAllDirectoryAccess(), new FakeMinioConfigurationProvider());
        }

        public MofangDbContext Db { get; }
        public FakeAssetStorage Storage { get; }
        public AccountContext Account { get; }
        public DamService Service { get; }

        public static async Task<TestContext> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MofangDbContext>().UseSqlite(connection).Options;
            var db = new MofangDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var accountId = Guid.NewGuid();
            db.Users.Add(new ApplicationUser
            {
                Id = accountId,
                UserName = "admin",
                NormalizedUserName = "ADMIN",
                DisplayName = "系统管理员",
                IsMasterAdmin = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return new TestContext(connection, db, new FakeAssetStorage(), new AccountContext(accountId, "admin", "系统管理员", true));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class FakeAssetStorage : IAssetStorage
    {
        public HashSet<(string Bucket, string ObjectKey)> DeletedObjects { get; } = [];
        public bool FailDeletes { get; set; }

        public Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
        {
            if (FailDeletes) throw new IOException("storage unavailable");
            DeletedObjects.Add((bucket, objectKey));
            return Task.CompletedTask;
        }

        public Task EnsureReadyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageWriteResult> UploadAsync(string bucket, string objectKey, Stream content, long size, string contentType, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DownloadToAsync(string bucket, string objectKey, Stream destination, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CopyAsync(string sourceBucket, string sourceObjectKey, string destinationBucket, string destinationObjectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StorageObjectMetadata> GetMetadataAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetDownloadUrlAsync(string bucket, string objectKey, string fileName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> GetPreviewUrlAsync(string bucket, string objectKey, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeThumbnailQueue : IThumbnailQueue
    {
        public ValueTask EnqueueAsync(Guid assetId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class AllowAllDirectoryAccess : IDirectoryAccessService
    {
        public Task<FolderAccessSnapshot> GetAccessAsync(AccountContext account, CancellationToken cancellationToken) =>
            Task.FromResult(new FolderAccessSnapshot(true, true, new Dictionary<Guid, FolderAccess>()));

        public Task EnsureCanViewAsync(AccountContext account, Guid? folderId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EnsureCanOperateAsync(AccountContext account, Guid? folderId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeMinioConfigurationProvider : IMinioConfigurationProvider
    {
        public Task<ResolvedMinioConfiguration> GetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
