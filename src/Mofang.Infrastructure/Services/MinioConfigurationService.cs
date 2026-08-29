using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mofang.Application;
using Mofang.Contracts;
using Mofang.Domain;
using Mofang.Infrastructure.Persistence;
using Mofang.Infrastructure.Storage;

namespace Mofang.Infrastructure.Services;

public sealed partial class MinioConfigurationService(
    MofangDbContext db,
    IOptions<MinioOptions> deploymentOptions,
    IDataProtectionProvider dataProtectionProvider,
    IThumbnailQueue thumbnailQueue,
    ILogger<MinioConfigurationService> logger) : IMinioConfigurationProvider, IMinioAdministrationService
{
    private const int ConfigurationId = 1;
    private static readonly Guid OperationTargetId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly IDataProtector _secretProtector = dataProtectionProvider.CreateProtector("Mofang.Dam.MinioConfiguration.SecretKey.v1");
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private ResolvedMinioConfiguration? _resolved;
    private MinioConfiguration? _stored;
    private bool _loaded;

    public async Task<ResolvedMinioConfiguration> GetAsync(CancellationToken cancellationToken = default)
    {
        await LoadAsync(cancellationToken);
        return _resolved!;
    }

    async Task<MinioSettingsDto> IMinioAdministrationService.GetAsync(CancellationToken cancellationToken)
    {
        var resolved = await GetAsync(cancellationToken);
        return ToDto(resolved, _stored);
    }

    public async Task<MinioConnectionTestDto> TestAsync(UpdateMinioSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var candidate = await BuildCandidateAsync(request, cancellationToken);
        await EnsureAvailableAsync(candidate, cancellationToken);
        return new MinioConnectionTestDto(true, "连接成功，资产 bucket 与缩略图 bucket 均可用。");
    }

    public async Task<MinioSettingsDto> UpdateAsync(UpdateMinioSettingsRequest request, AccountContext actor, CancellationToken cancellationToken = default)
    {
        if (!actor.IsMasterAdmin) throw new UnauthorizedAccessException("只有主账号可以修改 MinIO 配置。");
        var candidate = await BuildCandidateAsync(request, cancellationToken);
        await EnsureAvailableAsync(candidate, cancellationToken);

        var stored = await db.MinioConfigurations.SingleOrDefaultAsync(x => x.Id == ConfigurationId, cancellationToken);
        var previous = await GetAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (stored is null)
        {
            stored = new MinioConfiguration
            {
                Id = ConfigurationId,
                ServiceUrl = ToUrl(candidate.ServiceEndpoint),
                PublicUrl = ToUrl(candidate.PublicEndpoint),
                AccessKey = candidate.AccessKey,
                ProtectedSecretKey = _secretProtector.Protect(candidate.SecretKey),
                AssetBucket = candidate.AssetBucket,
                ThumbnailBucket = candidate.ThumbnailBucket,
                UpdatedAt = now,
                UpdatedBy = actor.UserName
            };
            db.MinioConfigurations.Add(stored);
        }
        else
        {
            stored.ServiceUrl = ToUrl(candidate.ServiceEndpoint);
            stored.PublicUrl = ToUrl(candidate.PublicEndpoint);
            stored.AccessKey = candidate.AccessKey;
            stored.ProtectedSecretKey = _secretProtector.Protect(candidate.SecretKey);
            stored.AssetBucket = candidate.AssetBucket;
            stored.ThumbnailBucket = candidate.ThumbnailBucket;
            stored.UpdatedAt = now;
            stored.UpdatedBy = actor.UserName;
        }

        OperationLogWriter.Add(db, actor, "ConfigureMinio", "System", OperationTargetId, "MinIO 存储配置", null, "/连接设置/MinIO", new
        {
            PreviousServiceUrl = ToUrl(previous.ServiceEndpoint),
            ServiceUrl = stored.ServiceUrl,
            PreviousPublicUrl = ToUrl(previous.PublicEndpoint),
            PublicUrl = stored.PublicUrl,
            stored.AccessKey,
            stored.AssetBucket,
            stored.ThumbnailBucket
        });
        await db.SaveChangesAsync(cancellationToken);

        var retryThumbnailIds = await db.Assets.AsNoTracking()
            .Where(x => x.Status == EntityStatus.Active && x.AssetType == AssetType.Image && x.ThumbnailStatus != ThumbnailStatus.Ready)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        foreach (var assetId in retryThumbnailIds) await thumbnailQueue.EnqueueAsync(assetId, cancellationToken);

        _stored = stored;
        _resolved = candidate;
        _loaded = true;
        return ToDto(candidate, stored);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded) return;

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            if (_loaded) return;

            _stored = await db.MinioConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ConfigurationId, cancellationToken);
            _resolved = _stored is null ? FromDeploymentOptions(deploymentOptions.Value) : FromStored(_stored, deploymentOptions.Value.PresignedUrlExpirySeconds);
            _loaded = true;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<ResolvedMinioConfiguration> BuildCandidateAsync(UpdateMinioSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var current = await GetAsync(cancellationToken);
        var secretKey = string.IsNullOrEmpty(request.SecretKey) ? current.SecretKey : request.SecretKey;
        if (string.IsNullOrWhiteSpace(secretKey) || secretKey.Length < 8) throw new ArgumentException("MinIO Secret Key 至少需要 8 个字符。");
        return new ResolvedMinioConfiguration(
            NormalizeEndpoint(request.ServiceUrl, "服务端访问地址"),
            NormalizeEndpoint(request.PublicUrl, "客户端公开地址"),
            NormalizeAccessKey(request.AccessKey),
            secretKey,
            NormalizeBucket(request.AssetBucket, "资产 bucket"),
            NormalizeBucket(request.ThumbnailBucket, "缩略图 bucket"),
            current.PresignedUrlExpirySeconds);
    }

    private async Task EnsureAvailableAsync(ResolvedMinioConfiguration candidate, CancellationToken cancellationToken)
    {
        try
        {
            var client = MinioClientFactory.Create(candidate.ServiceEndpoint, candidate.AccessKey, candidate.SecretKey);
            await MinioClientFactory.EnsureBucketsAsync(client, candidate, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "MinIO configuration check failed for {Endpoint}", candidate.ServiceEndpoint);
            throw new InvalidOperationException("无法连接 MinIO 或没有 bucket 管理权限，请检查服务端地址、访问密钥、网络和 TLS 设置。");
        }
    }

    private ResolvedMinioConfiguration FromStored(MinioConfiguration stored, int expirySeconds)
    {
        string secretKey;
        try
        {
            secretKey = _secretProtector.Unprotect(stored.ProtectedSecretKey);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unable to decrypt stored MinIO secret key");
            throw new InvalidOperationException("已保存的 MinIO Secret Key 无法解密，请由主账号重新配置。");
        }

        return new ResolvedMinioConfiguration(
            NormalizeEndpoint(stored.ServiceUrl, "服务端访问地址"),
            NormalizeEndpoint(stored.PublicUrl, "客户端公开地址"),
            NormalizeAccessKey(stored.AccessKey),
            secretKey,
            NormalizeBucket(stored.AssetBucket, "资产 bucket"),
            NormalizeBucket(stored.ThumbnailBucket, "缩略图 bucket"),
            expirySeconds);
    }

    private static ResolvedMinioConfiguration FromDeploymentOptions(MinioOptions options) => new(
        FromHostAndPort(options.Endpoint, options.Port, options.UseSSL, "Minio:Endpoint"),
        FromHostAndPort(options.PublicEndpoint, options.PublicPort, options.UseSSL, "Minio:PublicEndpoint"),
        NormalizeAccessKey(options.AccessKey),
        string.IsNullOrWhiteSpace(options.SecretKey) ? throw new InvalidOperationException("Minio:SecretKey is required.") : options.SecretKey,
        NormalizeBucket(options.BucketName, "Minio:BucketName"),
        NormalizeBucket(options.ThumbnailBucketName, "Minio:ThumbnailBucketName"),
        Math.Clamp(options.PresignedUrlExpirySeconds, 60, 604800));

    private static Uri FromHostAndPort(string host, int port, bool useSsl, string field)
    {
        if (Uri.TryCreate(host, UriKind.Absolute, out var absolute)) return NormalizeEndpoint(absolute.ToString(), field);
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) throw new InvalidOperationException($"{field} is invalid.");
        return NormalizeEndpoint(new UriBuilder(useSsl ? Uri.UriSchemeHttps : Uri.UriSchemeHttp, host.Trim(), port).Uri.ToString(), field);
    }

    private static Uri NormalizeEndpoint(string value, string field)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(endpoint.Host)
            || endpoint.Port is < 1 or > 65535
            || !string.IsNullOrEmpty(endpoint.UserInfo)
            || (!string.IsNullOrEmpty(endpoint.AbsolutePath) && endpoint.AbsolutePath != "/")
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
            throw new ArgumentException($"{field}必须是包含 http:// 或 https:// 的完整地址，且不能包含路径、查询参数或账号信息。");
        return new UriBuilder(endpoint.Scheme, endpoint.Host, endpoint.Port).Uri;
    }

    private static string NormalizeAccessKey(string value)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.Length is < 1 or > 255) throw new ArgumentException("MinIO Access Key 不能为空且不能超过 255 个字符。");
        return normalized;
    }

    private static string NormalizeBucket(string value, string field)
    {
        var normalized = value?.Trim() ?? "";
        if (!BucketNamePattern().IsMatch(normalized) || normalized.Contains("..", StringComparison.Ordinal) || IPAddress.TryParse(normalized, out _))
            throw new ArgumentException($"{field}必须为 3-63 位小写字母、数字、点或连字符，且不能使用 IP 地址格式。");
        return normalized;
    }

    private static string ToUrl(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Authority);

    private static MinioSettingsDto ToDto(ResolvedMinioConfiguration resolved, MinioConfiguration? stored) => new(
        ToUrl(resolved.ServiceEndpoint),
        ToUrl(resolved.PublicEndpoint),
        resolved.AccessKey,
        !string.IsNullOrEmpty(resolved.SecretKey),
        resolved.AssetBucket,
        resolved.ThumbnailBucket,
        stored is null ? "deployment" : "database",
        stored?.UpdatedAt,
        stored?.UpdatedBy);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{1,61}[a-z0-9])$")]
    private static partial Regex BucketNamePattern();
}
