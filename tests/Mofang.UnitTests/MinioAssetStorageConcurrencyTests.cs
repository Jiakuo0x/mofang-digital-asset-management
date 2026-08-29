using Mofang.Application;
using Mofang.Infrastructure.Storage;

namespace Mofang.UnitTests;

public sealed class MinioAssetStorageConcurrencyTests
{
    [Fact]
    public async Task ConcurrentPreviewRequests_LoadConfigurationOnce()
    {
        var provider = new BlockingConfigurationProvider();
        var storage = new MinioAssetStorage(provider);

        var requests = Enumerable.Range(0, 32)
            .Select(index => storage.GetPreviewUrlAsync("mofang-assets", $"assets/{index}.png"))
            .ToArray();

        await provider.FirstRequestStarted.WaitAsync(TimeSpan.FromSeconds(5));
        provider.Release();

        var urls = await Task.WhenAll(requests);

        Assert.Equal(1, provider.RequestCount);
        Assert.All(urls, url => Assert.StartsWith("http://127.0.0.1:9000/", url));
    }

    private sealed class BlockingConfigurationProvider : IMinioConfigurationProvider
    {
        private readonly TaskCompletionSource _firstRequestStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public Task FirstRequestStarted => _firstRequestStarted.Task;
        public int RequestCount => Volatile.Read(ref _requestCount);

        public async Task<ResolvedMinioConfiguration> GetAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _requestCount);
            _firstRequestStarted.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);

            return new ResolvedMinioConfiguration(
                new Uri("http://127.0.0.1:9000"),
                new Uri("http://127.0.0.1:9000"),
                "test-access-key",
                "test-secret-key",
                "mofang-assets",
                "mofang-thumbnails",
                3600);
        }

        public void Release() => _release.TrySetResult();
    }
}
