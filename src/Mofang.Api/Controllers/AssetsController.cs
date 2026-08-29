using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;

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
        CancellationToken cancellationToken = default) =>
        dam.SearchAssetsAsync(folderId, query, type, trash, createdFrom, createdTo, minSize, maxSize, sort, page, pageSize, User.ToAccountContext(), cancellationToken);

    [HttpGet("{id:guid}")]
    public Task<AssetDetailDto> Get(Guid id, CancellationToken cancellationToken) => dam.GetAssetAsync(id, User.ToAccountContext(), cancellationToken);

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<IReadOnlyList<AssetDto>>> Upload([FromForm] Guid? folderId, [FromForm] List<IFormFile> files, CancellationToken cancellationToken)
    {
        if (files.Count == 0) return BadRequest("请选择至少一个文件。");
        var results = new List<AssetDto>(files.Count);
        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            results.Add(await dam.UploadAsync(new UploadCommand(folderId, file.FileName, file.ContentType, file.Length, stream, User.ToAccountContext()), cancellationToken));
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
