namespace Mofang.Contracts;

public sealed record ApiInfoResponse(string Name, string Version, string Status, DateTimeOffset ServerTime);

public sealed record FolderDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool CanView,
    bool CanOperate,
    bool NavigationOnly,
    IReadOnlyList<FolderDto>? Children = null);

public sealed record CreateFolderRequest(Guid? ParentId, string Name);
public sealed record RenameRequest(string Name);
public sealed record MoveRequest(Guid? FolderId);
public sealed record RestoreRequest(Guid? FolderId = null);

public sealed record AssetDto(
    Guid Id,
    Guid? FolderId,
    string FileName,
    string OriginalFileName,
    string Extension,
    string MimeType,
    long FileSize,
    string AssetType,
    string Hash,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string PreviewUrl,
    string DownloadUrl,
    string? ThumbnailUrl,
    string ThumbnailStatus,
    bool CanOperate);

public sealed record AssetDetailDto(
    AssetDto Asset,
    string FolderPath,
    string Bucket,
    string ObjectKey,
    int CurrentVersionNumber);

public sealed record AssetPageDto(
    IReadOnlyList<AssetDto> Items,
    int Page,
    int PageSize,
    long Total);

public sealed record CopyAssetRequest(Guid? FolderId, string? Name = null);

public sealed record OperationLogDto(
    Guid Id,
    Guid? AccountId,
    string AccountName,
    string Action,
    string TargetType,
    Guid TargetId,
    string TargetName,
    Guid? FolderId,
    string FolderPath,
    string? PreviousName,
    string? NewName,
    string Detail,
    DateTimeOffset CreatedAt);

public sealed record OperationAccountOptionDto(Guid? Id, string Name);

public sealed record OperationLogPageDto(
    IReadOnlyList<OperationLogDto> Items,
    int Page,
    int PageSize,
    long Total,
    IReadOnlyList<OperationAccountOptionDto> Accounts);

public sealed record StorageSummaryDto(long AssetCount, long TotalBytes, long DeletedCount);

public sealed record MinioSettingsDto(
    string ServiceUrl,
    string PublicUrl,
    string AccessKey,
    bool SecretKeyConfigured,
    string AssetBucket,
    string ThumbnailBucket,
    string Source,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

public sealed record UpdateMinioSettingsRequest(
    string ServiceUrl,
    string PublicUrl,
    string AccessKey,
    string? SecretKey,
    string AssetBucket,
    string ThumbnailBucket);

public sealed record MinioConnectionTestDto(bool Success, string Message);

public sealed record SetupStatusDto(bool RequiresSetup);
public sealed record SetupAccountRequest(string UserName, string DisplayName, string Password);
public sealed record LoginRequest(string UserName, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AccountSessionDto(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsMasterAdmin,
    bool CanViewRoot,
    bool CanOperateRoot);

public sealed record AccountDto(
    Guid Id,
    string UserName,
    string DisplayName,
    bool IsMasterAdmin,
    bool IsEnabled,
    int PermissionCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record CreateAccountRequest(string UserName, string DisplayName, string Password);
public sealed record UpdateAccountRequest(string DisplayName, bool IsEnabled, string? UserName = null);
public sealed record ResetPasswordRequest(string Password);
public sealed record DirectoryPermissionDto(Guid? FolderId, bool CanView, bool CanOperate);
public sealed record ReplaceDirectoryPermissionsRequest(IReadOnlyList<DirectoryPermissionDto> Permissions);
public sealed record AccountPermissionsDto(Guid AccountId, IReadOnlyList<DirectoryPermissionDto> Permissions);
public sealed record DownloadLinkDto(string Url, string FileName);
