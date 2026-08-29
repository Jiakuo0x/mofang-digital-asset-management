using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mofang.Application;
using Mofang.Domain;
using Mofang.Infrastructure.Persistence;
using SkiaSharp;

namespace Mofang.Infrastructure.Jobs;

public sealed class ThumbnailWorker(
    IThumbnailQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ThumbnailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var assetId = await queue.DequeueAsync(stoppingToken);
                await ProcessAsync(assetId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Thumbnail worker loop failed");
            }
        }
    }

    private async Task ProcessAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MofangDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IAssetStorage>();
        var minio = await scope.ServiceProvider.GetRequiredService<IMinioConfigurationProvider>().GetAsync(cancellationToken);
        var asset = await db.Assets.SingleOrDefaultAsync(x => x.Id == assetId, cancellationToken);
        if (asset is null || asset.Status != EntityStatus.Active || asset.AssetType != AssetType.Image || asset.ThumbnailStatus == ThumbnailStatus.Ready) return;

        asset.ThumbnailStatus = ThumbnailStatus.Processing;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await using var source = await storage.OpenReadAsync(asset.Bucket, asset.ObjectKey, cancellationToken);
            using var bitmap = SKBitmap.Decode(source) ?? throw new InvalidDataException("Unsupported image format.");
            var scale = Math.Min(1d, Math.Min(720d / bitmap.Width, 480d / bitmap.Height));
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
            using var resized = bitmap.Resize(new SKImageInfo(width, height), SKSamplingOptions.Default) ?? throw new InvalidDataException("Unable to resize image.");
            using var thumbnailImage = SKImage.FromBitmap(resized);
            using var encoded = thumbnailImage.Encode(SKEncodedImageFormat.Webp, 78);
            await using var thumbnail = new MemoryStream();
            encoded.SaveTo(thumbnail);
            thumbnail.Position = 0;
            var objectKey = $"thumbnails/{asset.Id:N}.webp";
            await storage.UploadAsync(minio.ThumbnailBucket, objectKey, thumbnail, thumbnail.Length, "image/webp", cancellationToken);
            asset.ThumbnailBucket = minio.ThumbnailBucket;
            asset.ThumbnailObjectKey = objectKey;
            asset.ThumbnailStatus = ThumbnailStatus.Ready;
            asset.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Thumbnail generation failed for asset {AssetId}", assetId);
            asset.ThumbnailStatus = ThumbnailStatus.Failed;
            asset.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
