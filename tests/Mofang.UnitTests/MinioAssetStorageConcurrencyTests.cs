using Mofang.Application;
using Mofang.Infrastructure.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Mofang.UnitTests;

public sealed class MinioAssetStorageConcurrencyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RangeReadsAcceptValid206ButRejectTruncatedContent(bool truncate)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var app = builder.Build();
        var bytes = Enumerable.Range(0, 16).Select(x => (byte)x).ToArray();
        app.MapMethods("/{**path}", ["HEAD", "GET"], async context =>
        {
            context.Response.Headers.ETag = "\"test-object\"";
            context.Response.Headers.LastModified = DateTimeOffset.UtcNow.ToString("R");
            context.Response.ContentType = "video/mp4";
            if (context.Request.Method == "HEAD")
            {
                context.Response.ContentLength = bytes.Length;
                if (context.Request.Headers.ContainsKey("Range")) context.Response.StatusCode = 206;
                return;
            }
            Assert.Equal("bytes=4-7", context.Request.Headers.Range.ToString());
            context.Response.StatusCode = 206;
            context.Response.Headers.ContentRange = "bytes 4-7/16";
            var length = truncate ? 2 : 4;
            context.Response.ContentLength = length;
            await context.Response.Body.WriteAsync(bytes.AsMemory(4, length));
        });
        await app.StartAsync();
        try
        {
            var storage = new MinioAssetStorage(new RangeConfigurationProvider(new Uri(app.Urls.Single())));
            await using var result = new MemoryStream();
            if (truncate)
                await Assert.ThrowsAnyAsync<Exception>(() => storage.DownloadRangeToAsync("test-assets", "video.mp4", result, 4, 4));
            else
            {
                await storage.DownloadRangeToAsync("test-assets", "video.mp4", result, 4, 4);
                Assert.Equal(bytes[4..8], result.ToArray());
            }
        }
        finally { await app.StopAsync(); }
    }

    private sealed class RangeConfigurationProvider(Uri endpoint) : IMinioConfigurationProvider
    {
        public Task<ResolvedMinioConfiguration> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResolvedMinioConfiguration(endpoint, endpoint, "test-access", "test-secret", "test-assets", "test-thumbnails", 3600));
    }

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
