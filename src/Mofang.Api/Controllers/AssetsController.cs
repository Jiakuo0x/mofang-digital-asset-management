using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;
using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace Mofang.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/assets")]
public sealed class AssetsController(IDamService dam, IAssetStorage storage) : ControllerBase
{
    [HttpGet]
    public Task<AssetPageDto> Search(
        [FromQuery] Guid? folderId,
        [FromQuery] string? query,
        [FromQuery] string? type,
        [FromQuery] bool trash = false,
        [FromQuery] DateTimeOffset? createdFrom = null,
        [FromQuery] DateTimeOffset? createdTo = null,
        [FromQuery] long? minSize = null,
        [FromQuery] long? maxSize = null,
        [FromQuery] string sort = "updated",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string scope = "folder",
        CancellationToken cancellationToken = default) =>
        dam.SearchAssetsAsync(folderId, query, type, trash, createdFrom, createdTo, minSize, maxSize, sort, page, pageSize, User.ToAccountContext(), cancellationToken, scope);

    [HttpGet("{id:guid}")]
    public Task<AssetDetailDto> Get(Guid id, CancellationToken cancellationToken) => dam.GetAssetAsync(id, User.ToAccountContext(), cancellationToken);

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<IReadOnlyList<AssetDto>>> Upload([FromForm] Guid? folderId, [FromForm] List<IFormFile> files,
        [FromHeader(Name = "Idempotency-Key")] string? operationId, [FromForm] string? sourceContext, CancellationToken cancellationToken)
    {
        if (files.Count == 0) return BadRequest("请选择至少一个文件。");
        if (operationId is not null && (files.Count != 1 || operationId.Length is < 1 or > 128))
            return BadRequest("带操作标识的上传须逐文件提交。");
        if (sourceContext?.Length > 2048) return BadRequest("来源信息过长。");
        var results = new List<AssetDto>(files.Count);
        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            string? hash = null;
            if (operationId is not null)
            {
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                stream.Position = 0;
            }
            results.Add(await dam.UploadAsync(new UploadCommand(folderId, file.FileName, file.ContentType, file.Length, stream, User.ToAccountContext(), operationId, hash, sourceContext), cancellationToken));
        }
        return Created("/api/assets", results);
    }

    [HttpPut("{id:guid}/name")]
    public Task<AssetDto> Rename(Guid id, RenameRequest request, CancellationToken cancellationToken) => dam.RenameAssetAsync(id, request, User.ToAccountContext(), cancellationToken);

    [HttpPut("{id:guid}/move")]
    public Task<AssetDto> Move(Guid id, MoveRequest request, CancellationToken cancellationToken) => dam.MoveAssetAsync(id, request, User.ToAccountContext(), cancellationToken);

    [HttpPost("{id:guid}/copy")]
    public Task<AssetDto> Copy(Guid id, CopyAssetRequest request, CancellationToken cancellationToken) => dam.CopyAssetAsync(id, request, User.ToAccountContext(), cancellationToken);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await dam.DeleteAssetAsync(id, User.ToAccountContext(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> PermanentlyDelete(Guid id, CancellationToken cancellationToken)
    {
        await dam.PermanentlyDeleteAssetAsync(id, User.ToAccountContext(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public Task<AssetDto> Restore(Guid id, RestoreRequest request, CancellationToken cancellationToken) => dam.RestoreAssetAsync(id, request, User.ToAccountContext(), cancellationToken);

    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken) => Redirect(await dam.GetPreviewUrlAsync(id, false, User.ToAccountContext(), cancellationToken));

    [HttpGet("{id:guid}/thumbnail")]
    public async Task<IActionResult> Thumbnail(Guid id, CancellationToken cancellationToken) => Redirect(await dam.GetPreviewUrlAsync(id, true, User.ToAccountContext(), cancellationToken));

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> ContentStream(Guid id, [FromQuery] string variant = "original", CancellationToken cancellationToken = default)
    {
        if (variant is not ("original" or "thumbnail")) return BadRequest("媒体类型无效。");
        var descriptor = await dam.PrepareContentAsync(id, variant == "thumbnail", User.ToAccountContext(), cancellationToken);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.ContentType = descriptor.MimeType;
        long start = 0, length = descriptor.Size;
        if (Request.Headers.ContainsKey(HeaderNames.Range))
        {
            if (!RangeHeaderValue.TryParse(Request.Headers.Range.ToString(), out var range) || range.Unit != "bytes" || range.Ranges.Count != 1 || descriptor.Size == 0)
                return InvalidRange(descriptor.Size);
            var part = range.Ranges.Single();
            if (part.From.HasValue)
            {
                start = part.From.Value;
                var end = Math.Min(part.To ?? descriptor.Size - 1, descriptor.Size - 1);
                if (start >= descriptor.Size || start > end) return InvalidRange(descriptor.Size);
                length = end - start + 1;
            }
            else
            {
                if (!part.To.HasValue || part.To.Value <= 0) return InvalidRange(descriptor.Size);
                length = Math.Min(part.To.Value, descriptor.Size);
                start = descriptor.Size - length;
            }
            Response.StatusCode = 206;
            Response.Headers.ContentRange = $"bytes {start}-{start + length - 1}/{descriptor.Size}";
        }
        Response.ContentLength = length;
        if (Response.StatusCode == 206)
            await storage.DownloadRangeToAsync(descriptor.Bucket, descriptor.ObjectKey, Response.Body, start, length, cancellationToken);
        else
            await storage.DownloadToAsync(descriptor.Bucket, descriptor.ObjectKey, Response.Body, cancellationToken);
        return new EmptyResult();
    }

    private IActionResult InvalidRange(long size)
    {
        Response.Headers.ContentRange = $"bytes */{size}";
        return StatusCode(416);
    }

    [HttpGet("{id:guid}/download")]
    public async Task Download(Guid id, CancellationToken cancellationToken)
    {
        var descriptor = await dam.PrepareDownloadAsync(id, User.ToAccountContext(), cancellationToken);
        Response.ContentType = descriptor.MimeType;
        Response.ContentLength = descriptor.Size;
        Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(descriptor.FileName)}";
        await storage.DownloadToAsync(descriptor.Bucket, descriptor.ObjectKey, Response.Body, cancellationToken);
    }

    [HttpPost("{id:guid}/download-link")]
    public Task<DownloadLinkDto> DownloadLink(Guid id, CancellationToken cancellationToken) => dam.GetDownloadLinkAsync(id, User.ToAccountContext(), cancellationToken);

    [HttpGet("{id:guid}/text")]
    public async Task<IActionResult> Text(Guid id, CancellationToken cancellationToken) => Content(await dam.GetTextContentAsync(id, User.ToAccountContext(), cancellationToken), "text/plain; charset=utf-8");
}
