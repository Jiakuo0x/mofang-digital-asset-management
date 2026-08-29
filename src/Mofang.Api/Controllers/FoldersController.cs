using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;

namespace Mofang.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/folders")]
public sealed class FoldersController(IDamService dam) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<FolderDto>> GetTree([FromQuery] bool trash = false, CancellationToken cancellationToken = default) => dam.GetFolderTreeAsync(trash, User.ToAccountContext(), cancellationToken);

    [HttpPost]
    public async Task<ActionResult<FolderDto>> Create(CreateFolderRequest request, CancellationToken cancellationToken)
    {
        var result = await dam.CreateFolderAsync(request, User.ToAccountContext(), cancellationToken);
        return Created($"/api/folders/{result.Id}", result);
    }

    [HttpPut("{id:guid}/name")]
    public Task<FolderDto> Rename(Guid id, RenameRequest request, CancellationToken cancellationToken) => dam.RenameFolderAsync(id, request, User.ToAccountContext(), cancellationToken);

    [HttpPut("{id:guid}/move")]
    public Task<FolderDto> Move(Guid id, MoveRequest request, CancellationToken cancellationToken) => dam.MoveFolderAsync(id, request, User.ToAccountContext(), cancellationToken);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await dam.DeleteFolderAsync(id, User.ToAccountContext(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/permanent")]
    public async Task<IActionResult> PermanentlyDelete(Guid id, CancellationToken cancellationToken)
    {
        await dam.PermanentlyDeleteFolderAsync(id, User.ToAccountContext(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public Task<FolderDto> Restore(Guid id, RestoreRequest request, CancellationToken cancellationToken) => dam.RestoreFolderAsync(id, request, User.ToAccountContext(), cancellationToken);
}
