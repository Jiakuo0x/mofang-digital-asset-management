using Microsoft.EntityFrameworkCore;
using Mofang.Application;
using Mofang.Contracts;
using Mofang.Domain;
using Mofang.Infrastructure.Persistence;

namespace Mofang.Infrastructure.Services;

public sealed class DamService(
    MofangDbContext db,
    IAssetStorage storage,
    IThumbnailQueue thumbnailQueue,
    IDirectoryAccessService directoryAccess,
    IMinioConfigurationProvider minioConfiguration) : IDamService
{

    public async Task<IReadOnlyList<FolderDto>> GetFolderTreeAsync(bool trash, AccountContext account, CancellationToken cancellationToken)
    {
        var status = trash ? EntityStatus.Deleted : EntityStatus.Active;
        var access = await directoryAccess.GetAccessAsync(account, cancellationToken);
        var folders = await db.Folders.AsNoTracking().Where(x => x.Status == status).OrderBy(x => x.Name).ToListAsync(cancellationToken);
        folders = folders.Where(x => access.Folders.ContainsKey(x.Id)).ToList();
        var ids = folders.Select(x => x.Id).ToHashSet();
        return folders.Where(x => x.ParentId is null || !ids.Contains(x.ParentId.Value)).Select(x => ToFolderTree(x, folders, access)).ToList();
    }

    public async Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, AccountContext account, CancellationToken cancellationToken)
    {
        await directoryAccess.EnsureCanOperateAsync(account, request.ParentId, cancellationToken);
        var name = NormalizeName(request.Name);
        await EnsureFolderExistsAsync(request.ParentId, cancellationToken);
        await EnsureFolderNameAvailableAsync(request.ParentId, name, null, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var folder = new Folder { Id = Guid.NewGuid(), ParentId = request.ParentId, Name = name, CreatedBy = account.UserName, CreatedAt = now, UpdatedAt = now, Status = EntityStatus.Active };
        db.Folders.Add(folder);
        var folderPath = CombinePath(await GetFolderPathAsync(request.ParentId, cancellationToken), folder.Name);
        OperationLogWriter.Add(db, account, "CreateFolder", "Folder", folder.Id, folder.Name, folder.Id, folderPath, new { folder.Name, folder.ParentId, FolderPath = folderPath });
        await db.SaveChangesAsync(cancellationToken);
        return ToFolderDto(folder, new FolderAccess(true, true));
    }

    public async Task<FolderDto> RenameFolderAsync(Guid id, RenameRequest request, AccountContext account, CancellationToken cancellationToken)
    {
        await directoryAccess.EnsureCanOperateAsync(account, id, cancellationToken);
        var folder = await RequireFolderAsync(id, EntityStatus.Active, cancellationToken);
        var name = NormalizeName(request.Name);
        await EnsureFolderNameAvailableAsync(folder.ParentId, name, folder.Id, cancellationToken);
        var previousName = folder.Name;
        folder.Name = name;
        folder.UpdatedAt = DateTimeOffset.UtcNow;
        var folderPath = CombinePath(await GetFolderPathAsync(folder.ParentId, cancellationToken), name);
        OperationLogWriter.Add(db, account, "Rename", "Folder", folder.Id, name, folder.Id, folderPath, new { From = previousName, To = name, FolderPath = folderPath }, previousName, name);
        await db.SaveChangesAsync(cancellationToken);
        return ToFolderDto(folder, new FolderAccess(true, true));
    }

    public async Task<FolderDto> MoveFolderAsync(Guid id, MoveRequest request, AccountContext account, CancellationToken cancellationToken)
    {
        await directoryAccess.EnsureCanOperateAsync(account, id, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, request.FolderId, cancellationToken);
        var folder = await RequireFolderAsync(id, EntityStatus.Active, cancellationToken);
        if (request.FolderId == id) throw new InvalidOperationException("文件夹不能移动到自身。");
        await EnsureFolderExistsAsync(request.FolderId, cancellationToken);
        if (await IsDescendantAsync(request.FolderId, id, cancellationToken)) throw new InvalidOperationException("文件夹不能移动到自己的子目录。");
        await EnsureFolderNameAvailableAsync(request.FolderId, folder.Name, folder.Id, cancellationToken);
        var previousParentId = folder.ParentId;
        var previousPath = await GetFolderPathAsync(folder.Id, cancellationToken);
        folder.ParentId = request.FolderId;
        folder.UpdatedAt = DateTimeOffset.UtcNow;
        var folderPath = CombinePath(await GetFolderPathAsync(request.FolderId, cancellationToken), folder.Name);
        OperationLogWriter.Add(db, account, "Move", "Folder", folder.Id, folder.Name, folder.Id, folderPath, new { FromFolderId = previousParentId, ToFolderId = request.FolderId, FromFolderPath = previousPath, ToFolderPath = folderPath });
        await db.SaveChangesAsync(cancellationToken);
        return ToFolderDto(folder, new FolderAccess(true, true));
    }

    public async Task DeleteFolderAsync(Guid id, AccountContext account, CancellationToken cancellationToken)
    {
        await directoryAccess.EnsureCanOperateAsync(account, id, cancellationToken);
        var root = await RequireFolderAsync(id, EntityStatus.Active, cancellationToken);
        var folderPath = await GetFolderPathAsync(root.Id, cancellationToken);
        var allFolders = await db.Folders.Where(x => x.Status == EntityStatus.Active).ToListAsync(cancellationToken);
        var affectedIds = CollectDescendantIds(root.Id, allFolders);
        var now = DateTimeOffset.UtcNow;
        foreach (var folder in allFolders.Where(x => affectedIds.Contains(x.Id))) { folder.Status = EntityStatus.Deleted; folder.DeletedAt = now; folder.UpdatedAt = now; }
        var assets = await db.Assets.Where(x => x.Status == EntityStatus.Active && x.FolderId != null && affectedIds.Contains(x.FolderId.Value)).ToListAsync(cancellationToken);
        foreach (var asset in assets) { asset.Status = EntityStatus.Deleted; asset.DeletedAt = now; asset.UpdatedAt = now; }
        OperationLogWriter.Add(db, account, "Delete", "Folder", root.Id, root.Name, root.Id, folderPath, new { root.Name, FolderPath = folderPath, FolderCount = affectedIds.Count, AssetCount = assets.Count });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PermanentlyDeleteFolderAsync(Guid id, AccountContext account, CancellationToken cancellationToken)
    {
        await directoryAccess.EnsureCanOperateAsync(account, id, cancellationToken);
        var allFolders = await db.Folders.ToListAsync(cancellationToken);
        var root = allFolders.SingleOrDefault(x => x.Id == id && x.Status == EntityStatus.Deleted)
            ?? throw new KeyNotFoundException("回收站中的文件夹不存在。");
        var affectedIds = CollectDescendantIds(root.Id, allFolders);
        var affectedFolders = allFolders.Where(x => affectedIds.Contains(x.Id)).ToList();
        if (affectedFolders.Any(x => x.Status != EntityStatus.Deleted))
            throw new InvalidOperationException("文件夹中仍有未删除的子文件夹，无法彻底删除。");

        var assets = await db.Assets
            .Include(x => x.Versions)
            .ThenInclude(x => x.StorageObject)
            .Where(x => x.FolderId != null && affectedIds.Contains(x.FolderId.Value))
            .ToListAsync(cancellationToken);
        EnsureAssetsCanBePermanentlyDeleted(assets);

        var folderPath = await GetFolderPathAsync(root.Id, cancellationToken);
        var storageObjects = await GetExclusiveStorageObjectsAsync(assets, cancellationToken);
        await DeleteStoredFilesAsync(assets, storageObjects, cancellationToken);

        var permissions = await db.DirectoryPermissions.Where(x => x.FolderId != null && affectedIds.Contains(x.FolderId.Value)).ToListAsync(cancellationToken);
        OperationLogWriter.Add(db, account, "PermanentDelete", "Folder", root.Id, root.Name, root.Id, folderPath, new
        {
            root.Name,
            FolderPath = folderPath,
            FolderCount = affectedFolders.Count,
            AssetCount = assets.Count,
            StorageObjectCount = storageObjects.Count
        });
        db.DirectoryPermissions.RemoveRange(permissions);
        db.Assets.RemoveRange(assets);
        db.StorageObjects.RemoveRange(storageObjects);
        db.Folders.RemoveRange(affectedFolders);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<FolderDto> RestoreFolderAsync(Guid id, RestoreRequest request, AccountContext account, CancellationToken cancellationToken)
    {
        await directoryAccess.EnsureCanOperateAsync(account, id, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, request.FolderId, cancellationToken);
        var root = await RequireFolderAsync(id, EntityStatus.Deleted, cancellationToken);
        await EnsureFolderExistsAsync(request.FolderId, cancellationToken);
        await EnsureFolderNameAvailableAsync(request.FolderId, root.Name, root.Id, cancellationToken);
        var allFolders = await db.Folders.ToListAsync(cancellationToken);
        var affectedIds = CollectDescendantIds(root.Id, allFolders);
        var now = DateTimeOffset.UtcNow;
        root.ParentId = request.FolderId;
        foreach (var folder in allFolders.Where(x => affectedIds.Contains(x.Id))) { folder.Status = EntityStatus.Active; folder.DeletedAt = null; folder.UpdatedAt = now; }
        var assets = await db.Assets.Where(x => x.Status == EntityStatus.Deleted && x.FolderId != null && affectedIds.Contains(x.FolderId.Value)).ToListAsync(cancellationToken);
        foreach (var asset in assets) { asset.Status = EntityStatus.Active; asset.DeletedAt = null; asset.UpdatedAt = now; }
        var folderPath = CombinePath(await GetFolderPathAsync(request.FolderId, cancellationToken), root.Name);
        OperationLogWriter.Add(db, account, "Restore", "Folder", root.Id, root.Name, root.Id, folderPath, new { root.Name, FolderPath = folderPath, FolderCount = affectedIds.Count, AssetCount = assets.Count });
        await db.SaveChangesAsync(cancellationToken);
        return ToFolderDto(root, new FolderAccess(true, true));
    }

    public async Task<AssetPageDto> SearchAssetsAsync(Guid? folderId, string? query, string? type, bool trash, DateTimeOffset? createdFrom, DateTimeOffset? createdTo, long? minSize, long? maxSize, string sort, int page, int pageSize, AccountContext account, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var access = await directoryAccess.GetAccessAsync(account, cancellationToken);
        if (folderId.HasValue) await directoryAccess.EnsureCanViewAsync(account, folderId, cancellationToken);
        var status = trash ? EntityStatus.Deleted : EntityStatus.Active;
        var assets = db.Assets.AsNoTracking().Where(x => x.Status == status);
        if (folderId.HasValue) assets = assets.Where(x => x.FolderId == folderId);
        else if (!trash) assets = access.CanViewRoot ? assets.Where(x => x.FolderId == null) : assets.Where(x => false);
        else if (!account.IsMasterAdmin)
        {
            var visibleFolderIds = access.Folders.Where(x => x.Value.CanView).Select(x => x.Key).ToArray();
            assets = access.CanViewRoot
                ? assets.Where(x => x.FolderId == null || (x.FolderId != null && visibleFolderIds.Contains(x.FolderId.Value)))
                : assets.Where(x => x.FolderId != null && visibleFolderIds.Contains(x.FolderId.Value));
        }
        if (!string.IsNullOrWhiteSpace(query)) assets = assets.Where(x => EF.Functions.ILike(x.FileName, $"%{query.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<AssetType>(type, true, out var assetType)) assets = assets.Where(x => x.AssetType == assetType);
        if (createdFrom.HasValue) assets = assets.Where(x => x.CreatedAt >= createdFrom);
        if (createdTo.HasValue) assets = assets.Where(x => x.CreatedAt <= createdTo);
        if (minSize.HasValue) assets = assets.Where(x => x.FileSize >= minSize);
        if (maxSize.HasValue) assets = assets.Where(x => x.FileSize <= maxSize);
        assets = sort.ToLowerInvariant() switch { "name" => assets.OrderBy(x => x.FileName), "size" => assets.OrderByDescending(x => x.FileSize), "created" => assets.OrderByDescending(x => x.CreatedAt), _ => assets.OrderByDescending(x => x.UpdatedAt) };
        var total = await assets.LongCountAsync(cancellationToken);
        var rows = await assets.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = await Task.WhenAll(rows.Select(x => ToAssetDtoAsync(x, CanOperate(access, x.FolderId), cancellationToken)));
        return new AssetPageDto(items, page, pageSize, total);
    }

    public async Task<AssetDetailDto> GetAssetAsync(Guid id, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("资产不存在。");
        await directoryAccess.EnsureCanViewAsync(account, asset.FolderId, cancellationToken);
        var access = await directoryAccess.GetAccessAsync(account, cancellationToken);
        return new AssetDetailDto(await ToAssetDtoAsync(asset, CanOperate(access, asset.FolderId), cancellationToken), await GetFolderPathAsync(asset.FolderId, cancellationToken), asset.Bucket, asset.ObjectKey, asset.CurrentVersionNumber);
    }

    public async Task<AssetDto> UploadAsync(UploadCommand command, CancellationToken cancellationToken)
    {
        await directoryAccess.EnsureCanOperateAsync(command.Account, command.FolderId, cancellationToken);
        await EnsureFolderExistsAsync(command.FolderId, cancellationToken);
        var originalName = NormalizeName(command.FileName);
        var fileName = await GetUniqueAssetNameAsync(command.FolderId, originalName, null, cancellationToken);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var type = AssetClassifier.Classify(fileName, command.ContentType);
        var id = Guid.NewGuid();
        var objectKey = $"assets/{DateTime.UtcNow:yyyy/MM}/{id:N}{extension}";
        var minio = await minioConfiguration.GetAsync(cancellationToken);
        var write = await storage.UploadAsync(minio.AssetBucket, objectKey, command.Content, command.Size, command.ContentType, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var asset = new Asset { Id = id, FolderId = command.FolderId, FileName = fileName, OriginalFileName = originalName, Extension = extension, MimeType = string.IsNullOrWhiteSpace(command.ContentType) ? "application/octet-stream" : command.ContentType, FileSize = write.Size, AssetType = type, Bucket = minio.AssetBucket, ObjectKey = objectKey, Hash = write.Hash, Status = EntityStatus.Active, CreatedBy = command.Account.UserName, CreatedAt = now, UpdatedAt = now, CurrentVersionNumber = 1, ThumbnailStatus = AssetClassifier.NeedsThumbnail(type) ? ThumbnailStatus.Pending : ThumbnailStatus.NotRequired };
        var storageObject = new StorageObject { Id = Guid.NewGuid(), Provider = "MinIO", Bucket = asset.Bucket, ObjectKey = asset.ObjectKey, FileSize = asset.FileSize, Hash = asset.Hash, CreatedAt = now };
        asset.Versions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = asset.Id, StorageObjectId = storageObject.Id, StorageObject = storageObject, VersionNumber = 1, FileName = asset.FileName, MimeType = asset.MimeType, FileSize = asset.FileSize, Hash = asset.Hash, CreatedBy = command.Account.UserName, CreatedAt = now });
        db.Assets.Add(asset);
        var folderPath = await GetFolderPathAsync(asset.FolderId, cancellationToken);
        OperationLogWriter.Add(db, command.Account, "Upload", "Asset", asset.Id, asset.FileName, asset.FolderId, folderPath, new { asset.FileName, FolderPath = folderPath, asset.FileSize, asset.Hash });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch { await storage.DeleteAsync(asset.Bucket, asset.ObjectKey, CancellationToken.None); throw; }
        if (asset.ThumbnailStatus == ThumbnailStatus.Pending) await thumbnailQueue.EnqueueAsync(asset.Id, cancellationToken);
        return await ToAssetDtoAsync(asset, true, cancellationToken);
    }

    public async Task<AssetDto> RenameAssetAsync(Guid id, RenameRequest request, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(id, EntityStatus.Active, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, asset.FolderId, cancellationToken);
        var name = await GetUniqueAssetNameAsync(asset.FolderId, NormalizeName(request.Name), asset.Id, cancellationToken);
        var previousName = asset.FileName;
        asset.FileName = name;
        asset.Extension = Path.GetExtension(name).ToLowerInvariant();
        asset.AssetType = AssetClassifier.Classify(name, asset.MimeType);
        asset.UpdatedAt = DateTimeOffset.UtcNow;
        var folderPath = await GetFolderPathAsync(asset.FolderId, cancellationToken);
        OperationLogWriter.Add(db, account, "Rename", "Asset", asset.Id, name, asset.FolderId, folderPath, new { From = previousName, To = name, FolderPath = folderPath }, previousName, name);
        await db.SaveChangesAsync(cancellationToken);
        return await ToAssetDtoAsync(asset, true, cancellationToken);
    }

    public async Task<AssetDto> MoveAssetAsync(Guid id, MoveRequest request, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(id, EntityStatus.Active, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, asset.FolderId, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, request.FolderId, cancellationToken);
        await EnsureFolderExistsAsync(request.FolderId, cancellationToken);
        var previousName = asset.FileName;
        var previousFolderId = asset.FolderId;
        var previousPath = await GetFolderPathAsync(previousFolderId, cancellationToken);
        asset.FileName = await GetUniqueAssetNameAsync(request.FolderId, asset.FileName, asset.Id, cancellationToken);
        asset.FolderId = request.FolderId;
        asset.UpdatedAt = DateTimeOffset.UtcNow;
        var folderPath = await GetFolderPathAsync(asset.FolderId, cancellationToken);
        OperationLogWriter.Add(db, account, "Move", "Asset", asset.Id, asset.FileName, asset.FolderId, folderPath, new { FromFolderId = previousFolderId, ToFolderId = request.FolderId, FromFolderPath = previousPath, ToFolderPath = folderPath, FromName = previousName, ToName = asset.FileName }, previousName == asset.FileName ? null : previousName, previousName == asset.FileName ? null : asset.FileName);
        await db.SaveChangesAsync(cancellationToken);
        return await ToAssetDtoAsync(asset, true, cancellationToken);
    }

    public async Task<AssetDto> CopyAssetAsync(Guid id, CopyAssetRequest request, AccountContext account, CancellationToken cancellationToken)
    {
        var source = await RequireAssetAsync(id, EntityStatus.Active, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, source.FolderId, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, request.FolderId, cancellationToken);
        await EnsureFolderExistsAsync(request.FolderId, cancellationToken);
        var requestedName = string.IsNullOrWhiteSpace(request.Name) ? source.FileName : NormalizeName(request.Name);
        var name = await GetUniqueAssetNameAsync(request.FolderId, requestedName, null, cancellationToken);
        var newId = Guid.NewGuid();
        var objectKey = $"assets/{DateTime.UtcNow:yyyy/MM}/{newId:N}{source.Extension}";
        await storage.CopyAsync(source.Bucket, source.ObjectKey, source.Bucket, objectKey, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var copy = new Asset { Id = newId, FolderId = request.FolderId, FileName = name, OriginalFileName = source.OriginalFileName, Extension = source.Extension, MimeType = source.MimeType, FileSize = source.FileSize, AssetType = source.AssetType, Bucket = source.Bucket, ObjectKey = objectKey, Hash = source.Hash, Status = EntityStatus.Active, CreatedBy = account.UserName, CreatedAt = now, UpdatedAt = now, CurrentVersionNumber = 1, ThumbnailStatus = AssetClassifier.NeedsThumbnail(source.AssetType) ? ThumbnailStatus.Pending : ThumbnailStatus.NotRequired };
        var storageObject = new StorageObject { Id = Guid.NewGuid(), Provider = "MinIO", Bucket = copy.Bucket, ObjectKey = copy.ObjectKey, FileSize = copy.FileSize, Hash = copy.Hash, CreatedAt = now };
        copy.Versions.Add(new AssetVersion { Id = Guid.NewGuid(), AssetId = copy.Id, StorageObjectId = storageObject.Id, StorageObject = storageObject, VersionNumber = 1, FileName = copy.FileName, MimeType = copy.MimeType, FileSize = copy.FileSize, Hash = copy.Hash, CreatedBy = account.UserName, CreatedAt = now });
        db.Assets.Add(copy);
        var sourcePath = await GetFolderPathAsync(source.FolderId, cancellationToken);
        var folderPath = await GetFolderPathAsync(copy.FolderId, cancellationToken);
        OperationLogWriter.Add(db, account, "Copy", "Asset", copy.Id, copy.FileName, copy.FolderId, folderPath, new { SourceAssetId = source.Id, SourceName = source.FileName, SourceFolderPath = sourcePath, TargetName = copy.FileName, TargetFolderPath = folderPath });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch { await storage.DeleteAsync(copy.Bucket, copy.ObjectKey, CancellationToken.None); throw; }
        if (copy.ThumbnailStatus == ThumbnailStatus.Pending) await thumbnailQueue.EnqueueAsync(copy.Id, cancellationToken);
        return await ToAssetDtoAsync(copy, true, cancellationToken);
    }

    public async Task DeleteAssetAsync(Guid id, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(id, EntityStatus.Active, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, asset.FolderId, cancellationToken);
        asset.Status = EntityStatus.Deleted;
        asset.DeletedAt = DateTimeOffset.UtcNow;
        asset.UpdatedAt = asset.DeletedAt.Value;
        var folderPath = await GetFolderPathAsync(asset.FolderId, cancellationToken);
        OperationLogWriter.Add(db, account, "Delete", "Asset", asset.Id, asset.FileName, asset.FolderId, folderPath, new { asset.FileName, FolderPath = folderPath });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PermanentlyDeleteAssetAsync(Guid id, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await db.Assets
            .Include(x => x.Versions)
            .ThenInclude(x => x.StorageObject)
            .SingleOrDefaultAsync(x => x.Id == id && x.Status == EntityStatus.Deleted, cancellationToken)
            ?? throw new KeyNotFoundException("回收站中的资产不存在。");
        await directoryAccess.EnsureCanOperateAsync(account, asset.FolderId, cancellationToken);
        EnsureAssetsCanBePermanentlyDeleted([asset]);

        var folderPath = await GetFolderPathAsync(asset.FolderId, cancellationToken);
        var storageObjects = await GetExclusiveStorageObjectsAsync([asset], cancellationToken);
        await DeleteStoredFilesAsync([asset], storageObjects, cancellationToken);

        OperationLogWriter.Add(db, account, "PermanentDelete", "Asset", asset.Id, asset.FileName, asset.FolderId, folderPath, new
        {
            asset.FileName,
            FolderPath = folderPath,
            asset.FileSize,
            StorageObjectCount = storageObjects.Count,
            HasThumbnail = asset.ThumbnailBucket is not null && asset.ThumbnailObjectKey is not null
        });
        db.Assets.Remove(asset);
        db.StorageObjects.RemoveRange(storageObjects);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AssetDto> RestoreAssetAsync(Guid id, RestoreRequest request, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(id, EntityStatus.Deleted, cancellationToken);
        await directoryAccess.EnsureCanOperateAsync(account, asset.FolderId, cancellationToken);
        var targetFolderId = request.FolderId ?? (await FolderIsActiveAsync(asset.FolderId, cancellationToken) ? asset.FolderId : null);
        await directoryAccess.EnsureCanOperateAsync(account, targetFolderId, cancellationToken);
        await EnsureFolderExistsAsync(targetFolderId, cancellationToken);
        var previousName = asset.FileName;
        asset.FileName = await GetUniqueAssetNameAsync(targetFolderId, asset.FileName, asset.Id, cancellationToken);
        asset.FolderId = targetFolderId;
        asset.Status = EntityStatus.Active;
        asset.DeletedAt = null;
        asset.UpdatedAt = DateTimeOffset.UtcNow;
        var folderPath = await GetFolderPathAsync(asset.FolderId, cancellationToken);
        OperationLogWriter.Add(db, account, "Restore", "Asset", asset.Id, asset.FileName, asset.FolderId, folderPath, new { asset.FileName, FolderPath = folderPath }, previousName == asset.FileName ? null : previousName, previousName == asset.FileName ? null : asset.FileName);
        await db.SaveChangesAsync(cancellationToken);
        return await ToAssetDtoAsync(asset, true, cancellationToken);
    }

    public async Task<string> GetPreviewUrlAsync(Guid id, bool thumbnail, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(id, EntityStatus.Active, cancellationToken);
        await directoryAccess.EnsureCanViewAsync(account, asset.FolderId, cancellationToken);
        if (thumbnail && asset.ThumbnailStatus == ThumbnailStatus.Ready && asset.ThumbnailBucket is not null && asset.ThumbnailObjectKey is not null) return await storage.GetPreviewUrlAsync(asset.ThumbnailBucket, asset.ThumbnailObjectKey, cancellationToken);
        return await storage.GetPreviewUrlAsync(asset.Bucket, asset.ObjectKey, cancellationToken);
    }

    public async Task<AssetDownloadDescriptor> PrepareDownloadAsync(Guid id, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(id, EntityStatus.Active, cancellationToken);
        await directoryAccess.EnsureCanViewAsync(account, asset.FolderId, cancellationToken);
        var folderPath = await GetFolderPathAsync(asset.FolderId, cancellationToken);
        OperationLogWriter.Add(db, account, "Download", "Asset", asset.Id, asset.FileName, asset.FolderId, folderPath, new { asset.FileName, FolderPath = folderPath, asset.FileSize });
        await db.SaveChangesAsync(cancellationToken);
        return new AssetDownloadDescriptor(asset.FileName, asset.MimeType, asset.FileSize, asset.Bucket, asset.ObjectKey);
    }

    public async Task<DownloadLinkDto> GetDownloadLinkAsync(Guid id, AccountContext account, CancellationToken cancellationToken)
    {
        var descriptor = await PrepareDownloadAsync(id, account, cancellationToken);
        return new DownloadLinkDto(await storage.GetDownloadUrlAsync(descriptor.Bucket, descriptor.ObjectKey, descriptor.FileName, cancellationToken), descriptor.FileName);
    }

    public async Task<string> GetTextContentAsync(Guid id, AccountContext account, CancellationToken cancellationToken)
    {
        var asset = await RequireAssetAsync(id, EntityStatus.Active, cancellationToken);
        await directoryAccess.EnsureCanViewAsync(account, asset.FolderId, cancellationToken);
        if (!AssetClassifier.CanPreviewText(asset.Extension)) throw new InvalidOperationException("该文件类型不支持文本预览。");
        if (asset.FileSize > 2 * 1024 * 1024) throw new InvalidOperationException("文本文件超过 2 MB，请下载后查看。");
        await using var stream = await storage.OpenReadAsync(asset.Bucket, asset.ObjectKey, cancellationToken);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public async Task<OperationLogPageDto> GetOperationLogsAsync(Guid? folderId, string? directory, string? fileName, Guid? accountId, int page, int pageSize, AccountContext account, CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var access = await directoryAccess.GetAccessAsync(account, cancellationToken);
        var logs = db.OperationLogs.AsNoTracking().AsQueryable();
        if (!account.IsMasterAdmin)
        {
            var visibleFolderIds = access.Folders.Where(x => x.Value.CanView).Select(x => x.Key).ToArray();
            logs = access.CanViewRoot
                ? logs.Where(x => x.FolderId == null || (x.FolderId != null && visibleFolderIds.Contains(x.FolderId.Value)))
                : logs.Where(x => x.FolderId != null && visibleFolderIds.Contains(x.FolderId.Value));
        }
        var accountOptionRows = await logs.Where(x => x.AccountId != null).Select(x => new { x.AccountId, x.UserId }).Distinct().ToListAsync(cancellationToken);
        var accountOptions = accountOptionRows.Select(x => new OperationAccountOptionDto(x.AccountId, x.UserId)).OrderBy(x => x.Name).ToList();
        if (folderId.HasValue)
        {
            await directoryAccess.EnsureCanViewAsync(account, folderId, cancellationToken);
            var allFolders = await db.Folders.AsNoTracking().ToListAsync(cancellationToken);
            var folderIds = CollectDescendantIds(folderId.Value, allFolders);
            logs = logs.Where(x => x.FolderId != null && folderIds.Contains(x.FolderId.Value));
        }
        if (!string.IsNullOrWhiteSpace(directory)) logs = logs.Where(x => EF.Functions.ILike(x.FolderPath, $"%{directory.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var value = $"%{fileName.Trim()}%";
            logs = logs.Where(x => EF.Functions.ILike(x.TargetName, value) || (x.PreviousName != null && EF.Functions.ILike(x.PreviousName, value)) || (x.NewName != null && EF.Functions.ILike(x.NewName, value)));
        }
        if (accountId.HasValue) logs = logs.Where(x => x.AccountId == accountId);
        var total = await logs.LongCountAsync(cancellationToken);
        var items = await logs.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OperationLogDto(x.Id, x.AccountId, x.UserId, x.Action, x.TargetType, x.TargetId, x.TargetName, x.FolderId, x.FolderPath, x.PreviousName, x.NewName, x.Detail, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return new OperationLogPageDto(items, page, pageSize, total, accountOptions);
    }

    public async Task<StorageSummaryDto> GetStorageSummaryAsync(AccountContext account, CancellationToken cancellationToken)
    {
        var access = await directoryAccess.GetAccessAsync(account, cancellationToken);
        var assets = db.Assets.AsNoTracking().AsQueryable();
        if (!account.IsMasterAdmin)
        {
            var visibleFolderIds = access.Folders.Where(x => x.Value.CanView).Select(x => x.Key).ToArray();
            assets = access.CanViewRoot
                ? assets.Where(x => x.FolderId == null || (x.FolderId != null && visibleFolderIds.Contains(x.FolderId.Value)))
                : assets.Where(x => x.FolderId != null && visibleFolderIds.Contains(x.FolderId.Value));
        }
        return new StorageSummaryDto(await assets.LongCountAsync(x => x.Status == EntityStatus.Active, cancellationToken), await assets.Where(x => x.Status == EntityStatus.Active).SumAsync(x => (long?)x.FileSize, cancellationToken) ?? 0, await assets.LongCountAsync(x => x.Status == EntityStatus.Deleted, cancellationToken));
    }

    private static bool CanOperate(FolderAccessSnapshot access, Guid? folderId) => folderId.HasValue ? access.Folders.TryGetValue(folderId.Value, out var folder) && folder.CanOperate : access.CanOperateRoot;
    private static FolderDto ToFolderDto(Folder folder, FolderAccess access) => new(folder.Id, folder.ParentId, folder.Name, folder.Status.ToString(), folder.CreatedAt, folder.UpdatedAt, access.CanView, access.CanOperate, access.NavigationOnly);
    private static FolderDto ToFolderTree(Folder folder, IReadOnlyCollection<Folder> all, FolderAccessSnapshot access)
    {
        var permission = access.Folders[folder.Id];
        return new FolderDto(folder.Id, folder.ParentId, folder.Name, folder.Status.ToString(), folder.CreatedAt, folder.UpdatedAt, permission.CanView, permission.CanOperate, permission.NavigationOnly, all.Where(x => x.ParentId == folder.Id).OrderBy(x => x.Name).Select(x => ToFolderTree(x, all, access)).ToList());
    }

    private async Task<AssetDto> ToAssetDtoAsync(Asset asset, bool canOperate, CancellationToken cancellationToken)
    {
        var previewUrl = await storage.GetPreviewUrlAsync(asset.Bucket, asset.ObjectKey, cancellationToken);
        var thumbnailUrl = asset.ThumbnailStatus == ThumbnailStatus.Ready && asset.ThumbnailBucket is not null && asset.ThumbnailObjectKey is not null ? await storage.GetPreviewUrlAsync(asset.ThumbnailBucket, asset.ThumbnailObjectKey, cancellationToken) : null;
        return new AssetDto(asset.Id, asset.FolderId, asset.FileName, asset.OriginalFileName, asset.Extension, asset.MimeType, asset.FileSize, asset.AssetType.ToString(), asset.Hash, asset.Status.ToString(), asset.CreatedAt, asset.UpdatedAt, previewUrl, $"/api/assets/{asset.Id}/download", thumbnailUrl, asset.ThumbnailStatus.ToString(), canOperate);
    }

    private static string NormalizeName(string name)
    {
        var value = name.Trim();
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("名称不能为空。");
        if (value.Length > 255) throw new ArgumentException("名称不能超过 255 个字符。");
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value is "." or "..") throw new ArgumentException("名称包含无效字符。");
        return value;
    }

    private async Task EnsureFolderExistsAsync(Guid? id, CancellationToken cancellationToken) { if (id.HasValue && !await db.Folders.AnyAsync(x => x.Id == id && x.Status == EntityStatus.Active, cancellationToken)) throw new KeyNotFoundException("目标文件夹不存在或已删除。"); }
    private async Task<Folder> RequireFolderAsync(Guid id, EntityStatus status, CancellationToken cancellationToken) => await db.Folders.SingleOrDefaultAsync(x => x.Id == id && x.Status == status, cancellationToken) ?? throw new KeyNotFoundException("文件夹不存在。");
    private async Task<Asset> RequireAssetAsync(Guid id, EntityStatus status, CancellationToken cancellationToken) => await db.Assets.SingleOrDefaultAsync(x => x.Id == id && x.Status == status, cancellationToken) ?? throw new KeyNotFoundException("资产不存在。");
    private async Task EnsureFolderNameAvailableAsync(Guid? parentId, string name, Guid? excludedId, CancellationToken cancellationToken) { if (await db.Folders.AnyAsync(x => x.ParentId == parentId && x.Name == name && x.Status == EntityStatus.Active && x.Id != excludedId, cancellationToken)) throw new InvalidOperationException("同级目录中已存在同名文件夹。"); }

    private async Task<bool> IsDescendantAsync(Guid? candidateId, Guid ancestorId, CancellationToken cancellationToken)
    {
        while (candidateId.HasValue) { if (candidateId == ancestorId) return true; candidateId = await db.Folders.Where(x => x.Id == candidateId).Select(x => x.ParentId).SingleOrDefaultAsync(cancellationToken); }
        return false;
    }

    private static HashSet<Guid> CollectDescendantIds(Guid rootId, IReadOnlyCollection<Folder> folders)
    {
        var result = new HashSet<Guid> { rootId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootId);
        while (queue.TryDequeue(out var id)) foreach (var child in folders.Where(x => x.ParentId == id)) if (result.Add(child.Id)) queue.Enqueue(child.Id);
        return result;
    }

    private async Task<string> GetUniqueAssetNameAsync(Guid? folderId, string requested, Guid? excludedId, CancellationToken cancellationToken)
    {
        var existing = await db.Assets.Where(x => x.FolderId == folderId && x.Status == EntityStatus.Active && x.Id != excludedId).Select(x => x.FileName).ToListAsync(cancellationToken);
        var names = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(requested)) return requested;
        var extension = Path.GetExtension(requested);
        var stem = Path.GetFileNameWithoutExtension(requested);
        for (var index = 1; ; index++) { var candidate = $"{stem} ({index}){extension}"; if (!names.Contains(candidate)) return candidate; }
    }

    private async Task<bool> FolderIsActiveAsync(Guid? id, CancellationToken cancellationToken) => !id.HasValue || await db.Folders.AnyAsync(x => x.Id == id && x.Status == EntityStatus.Active, cancellationToken);

    private static void EnsureAssetsCanBePermanentlyDeleted(IReadOnlyCollection<Asset> assets)
    {
        if (assets.Any(x => x.Status != EntityStatus.Deleted))
            throw new InvalidOperationException("文件夹中仍有未删除的资产，无法彻底删除。");
        if (assets.Any(x => x.ThumbnailStatus == ThumbnailStatus.Processing))
            throw new InvalidOperationException("有资产的缩略图仍在处理中，请稍后重试。");
    }

    private async Task<List<StorageObject>> GetExclusiveStorageObjectsAsync(IReadOnlyCollection<Asset> assets, CancellationToken cancellationToken)
    {
        var assetIds = assets.Select(x => x.Id).ToHashSet();
        var storageObjects = assets.SelectMany(x => x.Versions).Select(x => x.StorageObject).DistinctBy(x => x.Id).ToList();
        if (storageObjects.Count == 0) return storageObjects;

        var storageObjectIds = storageObjects.Select(x => x.Id).ToArray();
        var sharedIds = await db.AssetVersions.AsNoTracking()
            .Where(x => storageObjectIds.Contains(x.StorageObjectId) && !assetIds.Contains(x.AssetId))
            .Select(x => x.StorageObjectId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var shared = sharedIds.ToHashSet();
        return storageObjects.Where(x => !shared.Contains(x.Id)).ToList();
    }

    private async Task DeleteStoredFilesAsync(IReadOnlyCollection<Asset> assets, IReadOnlyCollection<StorageObject> storageObjects, CancellationToken cancellationToken)
    {
        var objects = storageObjects.Select(x => (x.Bucket, x.ObjectKey)).ToHashSet();
        foreach (var asset in assets)
        {
            if (asset.ThumbnailBucket is not null && asset.ThumbnailObjectKey is not null)
                objects.Add((asset.ThumbnailBucket, asset.ThumbnailObjectKey));
        }

        foreach (var (bucket, objectKey) in objects)
            await storage.DeleteAsync(bucket, objectKey, cancellationToken);
    }

    private async Task<string> GetFolderPathAsync(Guid? folderId, CancellationToken cancellationToken)
    {
        if (!folderId.HasValue) return "/";
        var all = await db.Folders.AsNoTracking().ToDictionaryAsync(x => x.Id, cancellationToken);
        var parts = new Stack<string>();
        while (folderId.HasValue && all.TryGetValue(folderId.Value, out var folder)) { parts.Push(folder.Name); folderId = folder.ParentId; }
        return "/" + string.Join('/', parts);
    }

    private static string CombinePath(string parentPath, string name) => parentPath == "/" ? $"/{name}" : $"{parentPath}/{name}";
}
