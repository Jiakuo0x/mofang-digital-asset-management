using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Mofang.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
    public bool IsMasterAdmin { get; set; }
    public bool IsEnabled { get; set; } = true;
    public Guid? CreatedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

public sealed class DirectoryPermission
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public ApplicationUser Account { get; set; } = null!;
    public Guid? FolderId { get; set; }
    public Domain.Folder? Folder { get; set; }
    public bool CanView { get; set; }
    public bool CanOperate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class MofangClaimTypes
{
    public const string DisplayName = "mofang:display_name";
    public const string MasterAdmin = "mofang:master_admin";
}

public sealed class MofangClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(MofangClaimTypes.DisplayName, user.DisplayName));
        identity.AddClaim(new Claim(MofangClaimTypes.MasterAdmin, user.IsMasterAdmin ? "true" : "false"));
        return identity;
    }
}
