using Mofang.Contracts;

namespace Mofang.Application;

public sealed record StorageWriteResult(long Size, string Hash, string? ETag);
public sealed record StorageObjectMetadata(long Size, string? ContentType, string? ETag, DateTimeOffset? LastModified);
public sealed record AssetDownloadDescriptor(string FileName, string MimeType, long Size, string Bucket, string ObjectKey);
public sealed record AccountContext(Guid Id, string UserName, string DisplayName, bool IsMasterAdmin);
public sealed record FolderAccess(bool CanView, bool CanOperate, bool NavigationOnly = false);
public sealed record FolderAccessSnapshot(bool CanViewRoot, bool CanOperateRoot, IReadOnlyDictionary<Guid, FolderAccess> Folders);
public sealed record FolderAccessNode(Guid Id, Guid? ParentId);
public sealed record DirectoryGrant(Guid? FolderId, bool CanView, bool CanOperate);
public sealed record ResolvedMinioConfiguration(
    Uri ServiceEndpoint,
    Uri PublicEndpoint,
    string AccessKey,
    string SecretKey,
    string AssetBucket,
    string ThumbnailBucket,
    int PresignedUrlExpirySeconds);

public interface IAssetStorage
{
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
    Task<StorageWriteResult> UploadAsync(string bucket, string objectKey, Stream content, long size, string contentType, CancellationToken cancellationToken = default);
    Task<Stream> OpenReadAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);
    Task DownloadToAsync(string bucket, string objectKey, Stream destination, CancellationToken cancellationToken = default);
    Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);
    Task CopyAsync(string sourceBucket, string sourceObjectKey, string destinationBucket, string destinationObjectKey, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);
    Task<StorageObjectMetadata> GetMetadataAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);
    Task<string> GetDownloadUrlAsync(string bucket, string objectKey, string fileName, CancellationToken cancellationToken = default);
    Task<string> GetPreviewUrlAsync(string bucket, string objectKey, CancellationToken cancellationToken = default);
}

public interface IMinioConfigurationProvider
{
    Task<ResolvedMinioConfiguration> GetAsync(CancellationToken cancellationToken = default);
}

public interface IMinioAdministrationService
{
    Task<MinioSettingsDto> GetAsync(CancellationToken cancellationToken = default);
    Task<MinioConnectionTestDto> TestAsync(UpdateMinioSettingsRequest request, CancellationToken cancellationToken = default);
    Task<MinioSettingsDto> UpdateAsync(UpdateMinioSettingsRequest request, AccountContext actor, CancellationToken cancellationToken = default);
}

public sealed record UploadCommand(Guid? FolderId, string FileName, string ContentType, long Size, Stream Content, AccountContext Account);

public interface IDamService
{
    Task<IReadOnlyList<FolderDto>> GetFolderTreeAsync(bool trash, AccountContext account, CancellationToken cancellationToken);
    Task<FolderDto> CreateFolderAsync(CreateFolderRequest request, AccountContext account, CancellationToken cancellationToken);
    Task<FolderDto> RenameFolderAsync(Guid id, RenameRequest request, AccountContext account, CancellationToken cancellationToken);
    Task<FolderDto> MoveFolderAsync(Guid id, MoveRequest request, AccountContext account, CancellationToken cancellationToken);
    Task DeleteFolderAsync(Guid id, AccountContext account, CancellationToken cancellationToken);
    Task PermanentlyDeleteFolderAsync(Guid id, AccountContext account, CancellationToken cancellationToken);
    Task<FolderDto> RestoreFolderAsync(Guid id, RestoreRequest request, AccountContext account, CancellationToken cancellationToken);
    Task<AssetPageDto> SearchAssetsAsync(Guid? folderId, string? query, string? type, bool trash, DateTimeOffset? createdFrom, DateTimeOffset? createdTo, long? minSize, long? maxSize, string sort, int page, int pageSize, AccountContext account, CancellationToken cancellationToken);
    Task<AssetDetailDto> GetAssetAsync(Guid id, AccountContext account, CancellationToken cancellationToken);
    Task<AssetDto> UploadAsync(UploadCommand command, CancellationToken cancellationToken);
    Task<AssetDto> RenameAssetAsync(Guid id, RenameRequest request, AccountContext account, CancellationToken cancellationToken);
    Task<AssetDto> MoveAssetAsync(Guid id, MoveRequest request, AccountContext account, CancellationToken cancellationToken);
    Task<AssetDto> CopyAssetAsync(Guid id, CopyAssetRequest request, AccountContext account, CancellationToken cancellationToken);
    Task DeleteAssetAsync(Guid id, AccountContext account, CancellationToken cancellationToken);
    Task PermanentlyDeleteAssetAsync(Guid id, AccountContext account, CancellationToken cancellationToken);
    Task<AssetDto> RestoreAssetAsync(Guid id, RestoreRequest request, AccountContext account, CancellationToken cancellationToken);
    Task<string> GetPreviewUrlAsync(Guid id, bool thumbnail, AccountContext account, CancellationToken cancellationToken);
    Task<AssetDownloadDescriptor> PrepareDownloadAsync(Guid id, AccountContext account, CancellationToken cancellationToken);
    Task<DownloadLinkDto> GetDownloadLinkAsync(Guid id, AccountContext account, CancellationToken cancellationToken);
    Task<string> GetTextContentAsync(Guid id, AccountContext account, CancellationToken cancellationToken);
    Task<OperationLogPageDto> GetOperationLogsAsync(Guid? folderId, string? directory, string? fileName, Guid? accountId, int page, int pageSize, AccountContext account, CancellationToken cancellationToken);
    Task<StorageSummaryDto> GetStorageSummaryAsync(AccountContext account, CancellationToken cancellationToken);
}

public interface IDirectoryAccessService
{
    Task<FolderAccessSnapshot> GetAccessAsync(AccountContext account, CancellationToken cancellationToken);
    Task EnsureCanViewAsync(AccountContext account, Guid? folderId, CancellationToken cancellationToken);
    Task EnsureCanOperateAsync(AccountContext account, Guid? folderId, CancellationToken cancellationToken);
}

public interface ILocationService
{
    Task<LocationDto> GetAsync(string kind, Guid? id, AccountContext account, CancellationToken cancellationToken);
    Task<ResolveLocationResponse> ResolveAsync(string input, AccountContext account, CancellationToken cancellationToken);
}

public interface IAccountService
{
    Task<bool> RequiresSetupAsync(CancellationToken cancellationToken);
    Task<AccountDto> SetupMasterAsync(SetupAccountRequest request, CancellationToken cancellationToken);
    Task RecordLoginAsync(AccountContext account, CancellationToken cancellationToken);
    Task ChangeOwnPasswordAsync(ChangePasswordRequest request, AccountContext actor, CancellationToken cancellationToken);
    Task<AccountSessionDto> GetSessionAsync(AccountContext account, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken);
    Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, AccountContext actor, CancellationToken cancellationToken);
    Task<AccountDto> UpdateAccountAsync(Guid id, UpdateAccountRequest request, AccountContext actor, CancellationToken cancellationToken);
    Task DeleteAccountAsync(Guid id, AccountContext actor, CancellationToken cancellationToken);
    Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, AccountContext actor, CancellationToken cancellationToken);
    Task<AccountPermissionsDto> GetPermissionsAsync(Guid id, CancellationToken cancellationToken);
    Task<AccountPermissionsDto> ReplacePermissionsAsync(Guid id, ReplaceDirectoryPermissionsRequest request, AccountContext actor, CancellationToken cancellationToken);
}

public interface IThumbnailQueue
{
    ValueTask EnqueueAsync(Guid assetId, CancellationToken cancellationToken = default);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
