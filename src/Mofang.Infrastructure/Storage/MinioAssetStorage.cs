using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
using Mofang.Application;

namespace Mofang.Infrastructure.Storage;

public sealed class MinioAssetStorage(IMinioConfigurationProvider configurationProvider) : IAssetStorage
{
    private readonly SemaphoreSlim _clientsLock = new(1, 1);
    private MinioClients? _clients;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        await MinioClientFactory.EnsureBucketsAsync(clients.Internal, clients.Configuration, cancellationToken);
    }

    public async Task<StorageWriteResult> UploadAsync(string bucket, string objectKey, Stream content, long size, string contentType, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        using var hashingStream = new HashingReadStream(content);
        var response = await clients.Internal.PutObjectAsync(new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithStreamData(hashingStream)
            .WithObjectSize(size)
            .WithContentType(contentType), cancellationToken);
        return new StorageWriteResult(size, hashingStream.GetHash(), response.Etag);
    }

    public async Task<Stream> OpenReadAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        var path = Path.Combine(Path.GetTempPath(), $"mofang-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await clients.Internal.GetObjectAsync(new GetObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(objectKey)
                    .WithCallbackStream(stream => stream.CopyTo(output)), cancellationToken);
            }
            return new TemporaryFileStream(path);
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public async Task DownloadToAsync(string bucket, string objectKey, Stream destination, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        await clients.Internal.GetObjectAsync(new GetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithCallbackStream((source, token) => source.CopyToAsync(destination, token)), cancellationToken);
    }

    public async Task DeleteAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        await clients.Internal.RemoveObjectAsync(new RemoveObjectArgs().WithBucket(bucket).WithObject(objectKey), cancellationToken);
    }

    public async Task CopyAsync(string sourceBucket, string sourceObjectKey, string destinationBucket, string destinationObjectKey, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        var source = new CopySourceObjectArgs().WithBucket(sourceBucket).WithObject(sourceObjectKey);
        await clients.Internal.CopyObjectAsync(new CopyObjectArgs()
            .WithBucket(destinationBucket)
            .WithObject(destinationObjectKey)
            .WithCopyObjectSource(source), cancellationToken);
    }

    public async Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        try
        {
            await clients.Internal.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(objectKey), cancellationToken);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    public async Task<StorageObjectMetadata> GetMetadataAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        var stat = await clients.Internal.StatObjectAsync(new StatObjectArgs().WithBucket(bucket).WithObject(objectKey), cancellationToken);
        return new StorageObjectMetadata(stat.Size, stat.ContentType, stat.ETag, stat.LastModified);
    }

    public async Task<string> GetDownloadUrlAsync(string bucket, string objectKey, string fileName, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        return await clients.Public.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry(clients.Configuration.PresignedUrlExpirySeconds));
    }

    public async Task<string> GetPreviewUrlAsync(string bucket, string objectKey, CancellationToken cancellationToken = default)
    {
        var clients = await GetClientsAsync(cancellationToken);
        return await clients.Public.PresignedGetObjectAsync(new PresignedGetObjectArgs()
            .WithBucket(bucket)
            .WithObject(objectKey)
            .WithExpiry(clients.Configuration.PresignedUrlExpirySeconds));
    }

    private async Task<MinioClients> GetClientsAsync(CancellationToken cancellationToken)
    {
        if (_clients is not null) return _clients;

        await _clientsLock.WaitAsync(cancellationToken);
        try
        {
            if (_clients is not null) return _clients;

            var configuration = await configurationProvider.GetAsync(cancellationToken);
            _clients = new MinioClients(
                configuration,
                MinioClientFactory.Create(configuration.ServiceEndpoint, configuration.AccessKey, configuration.SecretKey),
                MinioClientFactory.Create(configuration.PublicEndpoint, configuration.AccessKey, configuration.SecretKey));
            return _clients;
        }
        finally
        {
            _clientsLock.Release();
        }
    }

    private sealed record MinioClients(ResolvedMinioConfiguration Configuration, IMinioClient Internal, IMinioClient Public);
}

internal static class MinioClientFactory
{
    public static IMinioClient Create(Uri endpoint, string accessKey, string secretKey) => new MinioClient()
        .WithEndpoint(endpoint.Host, endpoint.Port)
        .WithCredentials(accessKey, secretKey)
        .WithSSL(string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        .Build();

    public static async Task EnsureBucketsAsync(IMinioClient client, ResolvedMinioConfiguration configuration, CancellationToken cancellationToken)
    {
        await EnsureBucketAsync(client, configuration.AssetBucket, cancellationToken);
        if (!string.Equals(configuration.AssetBucket, configuration.ThumbnailBucket, StringComparison.Ordinal))
            await EnsureBucketAsync(client, configuration.ThumbnailBucket, cancellationToken);
    }

    private static async Task EnsureBucketAsync(IMinioClient client, string bucket, CancellationToken cancellationToken)
    {
        var exists = await client.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucket), cancellationToken);
        if (!exists) await client.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucket), cancellationToken);
    }
}
