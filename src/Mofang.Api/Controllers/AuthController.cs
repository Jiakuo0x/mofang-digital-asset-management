using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Mofang.Api.Security;
using Mofang.Application;
using Mofang.Contracts;
using Mofang.Infrastructure.Identity;

namespace Mofang.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IAccountService accounts,
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("setup-status")]
    public async Task<SetupStatusDto> SetupStatus(CancellationToken cancellationToken) => new(await accounts.RequiresSetupAsync(cancellationToken));

    [AllowAnonymous]
    [HttpPost("setup")]
    public async Task<ActionResult<AccountDto>> Setup(SetupAccountRequest request, CancellationToken cancellationToken)
    {
        var account = await accounts.SetupMasterAsync(request, cancellationToken);
        return Created($"/api/admin/accounts/{account.Id}", account);
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(request.UserName.Trim());
        if (user is null || !user.IsEnabled) return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "账号或密码错误。");
        var result = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Problem(statusCode: StatusCodes.Status401Unauthorized, title: result.IsLockedOut ? "登录失败次数过多，请稍后重试。" : "账号或密码错误。");

        var account = new AccountContext(user.Id, user.UserName!, user.DisplayName, user.IsMasterAdmin);
        await accounts.RecordLoginAsync(account, cancellationToken);
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signInManager.SignInAsync(user, isPersistent: false);
        return new EmptyResult();
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var account = User.ToAccountContext();
        await accounts.ChangeOwnPasswordAsync(request, account, cancellationToken);
        var user = await userManager.FindByIdAsync(account.Id.ToString()) ?? throw new KeyNotFoundException("账号不存在。");
        signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signInManager.SignInAsync(user, isPersistent: false);
        return new EmptyResult();
    }

    [Authorize]
    [HttpGet("session")]
    public Task<AccountSessionDto> Session(CancellationToken cancellationToken) => accounts.GetSessionAsync(User.ToAccountContext(), cancellationToken);
}
