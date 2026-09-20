using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;

namespace Mofang.Api.Controllers;

[ApiController]
[Authorize(Policy = MofangPolicies.MasterAdmin)]
[Route("api/admin/accounts")]
public sealed class AccountsController(IAccountService accounts) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AccountDto>> Get(CancellationToken cancellationToken) => accounts.GetAccountsAsync(cancellationToken);

    [HttpPost]
    public async Task<ActionResult<AccountDto>> Create(CreateAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.CreateAccountAsync(request, User.ToAccountContext(), cancellationToken);
        return Created($"/api/admin/accounts/{account.Id}", account);
    }

    [HttpPut("{id:guid}")]
    public Task<AccountDto> Update(Guid id, UpdateAccountRequest request, CancellationToken cancellationToken) => accounts.UpdateAccountAsync(id, request, User.ToAccountContext(), cancellationToken);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await accounts.DeleteAccountAsync(id, User.ToAccountContext(), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await accounts.ResetPasswordAsync(id, request, User.ToAccountContext(), cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/permissions")]
    public Task<AccountPermissionsDto> Permissions(Guid id, CancellationToken cancellationToken) => accounts.GetPermissionsAsync(id, cancellationToken);

    [HttpPut("{id:guid}/permissions")]
    public Task<AccountPermissionsDto> ReplacePermissions(Guid id, ReplaceDirectoryPermissionsRequest request, CancellationToken cancellationToken) => accounts.ReplacePermissionsAsync(id, request, User.ToAccountContext(), cancellationToken);
}
