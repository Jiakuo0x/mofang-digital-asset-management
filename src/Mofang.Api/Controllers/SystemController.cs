using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;
using Mofang.Infrastructure.Persistence;

namespace Mofang.Api.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class SystemController(MofangDbContext db, IAssetStorage storage, IDamService dam) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("info")]
    public ApiInfoResponse Info() => new("魔方数字资产管理", GetVersion(), "ok", DateTimeOffset.UtcNow);

    [AllowAnonymous]
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var database = await db.Database.CanConnectAsync(cancellationToken);
        try
        {
            await storage.EnsureReadyAsync(cancellationToken);
            return Ok(new { status = database ? "healthy" : "degraded", database, storage = true, time = DateTimeOffset.UtcNow });
        }
        catch
        {
            return StatusCode(503, new { status = "unhealthy", database, storage = false, time = DateTimeOffset.UtcNow });
        }
    }

    [HttpGet("operations")]
    public Task<OperationLogPageDto> Operations(
        [FromQuery] Guid? folderId = null,
        [FromQuery] string? directory = null,
        [FromQuery] string? fileName = null,
        [FromQuery] Guid? accountId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default) =>
        dam.GetOperationLogsAsync(folderId, directory, fileName, accountId, page, pageSize, User.ToAccountContext(), cancellationToken);

    [HttpGet("storage/summary")]
    public Task<StorageSummaryDto> StorageSummary(CancellationToken cancellationToken) => dam.GetStorageSummaryAsync(User.ToAccountContext(), cancellationToken);

    private static string GetVersion() => typeof(SystemController).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";
}
