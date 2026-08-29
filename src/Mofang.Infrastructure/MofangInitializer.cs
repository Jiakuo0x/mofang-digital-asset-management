using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mofang.Application;
using Mofang.Domain;
using Mofang.Infrastructure.Persistence;

namespace Mofang.Infrastructure;

public static class MofangInitializer
{
    public static async Task InitializeMofangAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MofangDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        var storage = scope.ServiceProvider.GetRequiredService<IAssetStorage>();
        try
        {
            await storage.EnsureReadyAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("MofangInitializer");
            logger.LogWarning(exception, "MinIO is unavailable during startup; the API will remain available for master-account reconfiguration");
        }
        var queue = scope.ServiceProvider.GetRequiredService<IThumbnailQueue>();
        var pending = await db.Assets.AsNoTracking()
            .Where(x => x.Status == EntityStatus.Active && x.AssetType == AssetType.Image && x.ThumbnailStatus != ThumbnailStatus.Ready)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var assetId in pending) await queue.EnqueueAsync(assetId, cancellationToken);
    }
}
