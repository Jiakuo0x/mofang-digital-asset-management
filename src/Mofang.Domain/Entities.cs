namespace Mofang.Domain;

public enum EntityStatus
{
    Active = 0,
    Deleted = 1
}

public sealed class LibraryIdentity
{
    public int Id { get; set; }
    public Guid LibraryId { get; set; }
}

public enum AssetType
{
    Image = 0,
    Video = 1,
    Audio = 2,
    Document = 3,
    Model3D = 4,
    Archive = 5,
    ProjectFile = 6,
    Other = 7
}

public enum ThumbnailStatus
{
    NotRequired = 0,
    Pending = 1,
    Processing = 2,
    Ready = 3,
    Failed = 4
}

public sealed class Folder
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public Folder? Parent { get; set; }
    public List<Folder> Children { get; set; } = [];
    public List<Asset> Assets { get; set; } = [];
    public required string Name { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public EntityStatus Status { get; set; }
}

public sealed class Asset
{
    public Guid Id { get; set; }
    public Guid? FolderId { get; set; }
    public Folder? Folder { get; set; }
    public required string FileName { get; set; }
    public required string OriginalFileName { get; set; }
    public required string Extension { get; set; }
    public required string MimeType { get; set; }
    public long FileSize { get; set; }
    public AssetType AssetType { get; set; }
    public required string Bucket { get; set; }
    public required string ObjectKey { get; set; }
    public required string Hash { get; set; }
    public EntityStatus Status { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public int CurrentVersionNumber { get; set; }
    public string? ThumbnailBucket { get; set; }
    public string? ThumbnailObjectKey { get; set; }
    public ThumbnailStatus ThumbnailStatus { get; set; }
    public List<AssetVersion> Versions { get; set; } = [];
}

public sealed class AssetVersion
{
    public Guid Id { get; set; }
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;
    public Guid StorageObjectId { get; set; }
    public StorageObject StorageObject { get; set; } = null!;
    public int VersionNumber { get; set; }
    public required string FileName { get; set; }
    public required string MimeType { get; set; }
    public long FileSize { get; set; }
    public required string Hash { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class StorageObject
{
    public Guid Id { get; set; }
    public required string Provider { get; set; }
    public required string Bucket { get; set; }
    public required string ObjectKey { get; set; }
    public long FileSize { get; set; }
    public required string Hash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<AssetVersion> Versions { get; set; } = [];
}

public sealed class MinioConfiguration
{
    public int Id { get; set; }
    public required string ServiceUrl { get; set; }
    public required string PublicUrl { get; set; }
    public required string AccessKey { get; set; }
    public required string ProtectedSecretKey { get; set; }
    public required string AssetBucket { get; set; }
    public required string ThumbnailBucket { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public required string UpdatedBy { get; set; }
}

public sealed class OperationLog
{
    public Guid Id { get; set; }
    public Guid? AccountId { get; set; }
    public required string UserId { get; set; }
    public required string Action { get; set; }
    public required string TargetType { get; set; }
    public Guid TargetId { get; set; }
    public required string TargetName { get; set; }
    public Guid? FolderId { get; set; }
    public required string FolderPath { get; set; }
    public string? PreviousName { get; set; }
    public string? NewName { get; set; }
    public required string Detail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
