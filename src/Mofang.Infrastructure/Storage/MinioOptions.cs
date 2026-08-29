namespace Mofang.Infrastructure.Storage;

public sealed class MinioOptions
{
    public const string SectionName = "Minio";
    public string Endpoint { get; set; } = "localhost";
    public int Port { get; set; } = 9000;
    public string PublicEndpoint { get; set; } = "localhost";
    public int PublicPort { get; set; } = 9000;
    public string AccessKey { get; set; } = "mofangadmin";
    public string SecretKey { get; set; } = "change-this-minio-secret";
    public bool UseSSL { get; set; }
    public string BucketName { get; set; } = "mofang-assets";
    public string ThumbnailBucketName { get; set; } = "mofang-thumbnails";
    public int PresignedUrlExpirySeconds { get; set; } = 3600;
}
