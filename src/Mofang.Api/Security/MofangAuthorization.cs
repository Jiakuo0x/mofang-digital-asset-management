using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Mofang.Application;
using Mofang.Infrastructure.Identity;
using Mofang.Infrastructure.Persistence;

namespace Mofang.Api.Security;

public static class MofangPolicies
{
    public const string MasterAdmin = "MasterAdmin";
}

public sealed class ActiveAccountRequirement : IAuthorizationRequirement;

public sealed class ActiveAccountHandler(MofangDbContext db) : AuthorizationHandler<ActiveAccountRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveAccountRequirement requirement)
    {
        var value = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(value, out var id) && await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.IsEnabled)) context.Succeed(requirement);
    }
}

public static class ClaimsPrincipalExtensions
{
    public static AccountContext ToAccountContext(this ClaimsPrincipal principal)
    {
        var idValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idValue, out var id)) throw new UnauthorizedAccessException("登录状态无效。");
        return new AccountContext(
            id,
            principal.FindFirstValue(ClaimTypes.Name) ?? throw new UnauthorizedAccessException("登录状态无效。"),
            principal.FindFirstValue(MofangClaimTypes.DisplayName) ?? principal.Identity?.Name ?? "",
            string.Equals(principal.FindFirstValue(MofangClaimTypes.MasterAdmin), "true", StringComparison.OrdinalIgnoreCase));
    }
}
