using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mofang.Application;
using Mofang.Contracts;
using Mofang.Infrastructure.Identity;
using Mofang.Infrastructure.Persistence;

namespace Mofang.Infrastructure.Services;

public sealed class AccountService(
    MofangDbContext db,
    UserManager<ApplicationUser> userManager,
    IDirectoryAccessService directoryAccess) : IAccountService
{
    public async Task<bool> RequiresSetupAsync(CancellationToken cancellationToken) =>
        !await db.Users.AsNoTracking().AnyAsync(cancellationToken);

    public async Task<AccountDto> SetupMasterAsync(SetupAccountRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await db.Users.AnyAsync(cancellationToken)) throw new InvalidOperationException("主账号已经初始化。");

        var user = CreateUser(request.UserName, request.DisplayName, true, null);
        ThrowIfFailed(await userManager.CreateAsync(user, request.Password));
        var actor = ToContext(user);
        OperationLogWriter.Add(db, actor, "SetupMaster", "Account", user.Id, user.UserName!, null, "/账户管理", new { user.UserName, user.DisplayName, user.IsMasterAdmin });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToDto(user, 0);
    }

    public async Task RecordLoginAsync(AccountContext account, CancellationToken cancellationToken)
    {
        var user = await RequireAccountAsync(account.Id, cancellationToken);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = user.LastLoginAt.Value;
        OperationLogWriter.Add(db, account, "Login", "Account", user.Id, user.UserName!, null, "/登录", new { user.UserName, user.DisplayName });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangeOwnPasswordAsync(ChangePasswordRequest request, AccountContext actor, CancellationToken cancellationToken)
    {
        var user = await RequireAccountAsync(actor.Id, cancellationToken);
        ThrowIfFailed(await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword));
        user.UpdatedAt = DateTimeOffset.UtcNow;
        OperationLogWriter.Add(db, actor, "ChangePassword", "Account", user.Id, user.UserName!, null, "/个人账户", new { user.UserName });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AccountSessionDto> GetSessionAsync(AccountContext account, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == account.Id && x.IsEnabled, cancellationToken)
            ?? throw new UnauthorizedAccessException("账号已停用或不存在。");
        var access = await directoryAccess.GetAccessAsync(account, cancellationToken);
        return new AccountSessionDto(user.Id, user.UserName!, user.DisplayName, user.IsMasterAdmin, access.CanViewRoot, access.CanOperateRoot);
    }

    public async Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderByDescending(x => x.IsMasterAdmin).ThenBy(x => x.UserName)
            .Select(x => new AccountDto(
                x.Id,
                x.UserName!,
                x.DisplayName,
                x.IsMasterAdmin,
                x.IsEnabled,
                db.DirectoryPermissions.Count(p => p.AccountId == x.Id),
                x.CreatedAt,
                x.LastLoginAt))
            .ToListAsync(cancellationToken);

    public async Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, AccountContext actor, CancellationToken cancellationToken)
    {
        var user = CreateUser(request.UserName, request.DisplayName, false, actor.Id);
        ThrowIfFailed(await userManager.CreateAsync(user, request.Password));
        OperationLogWriter.Add(db, actor, "CreateAccount", "Account", user.Id, user.UserName!, null, "/账户管理", new { user.UserName, user.DisplayName, user.IsEnabled });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(user, 0);
    }

    public async Task<AccountDto> UpdateAccountAsync(Guid id, UpdateAccountRequest request, AccountContext actor, CancellationToken cancellationToken)
    {
        var user = await RequireAccountAsync(id, cancellationToken);
        if (user.IsMasterAdmin && !request.IsEnabled) throw new InvalidOperationException("主账号不能停用。");
        if (user.Id == actor.Id && !request.IsEnabled) throw new InvalidOperationException("不能停用当前登录账号。");
        var previousDisplayName = user.DisplayName;
        var previousEnabled = user.IsEnabled;
        user.DisplayName = NormalizeDisplayName(request.DisplayName);
        user.IsEnabled = request.IsEnabled;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        ThrowIfFailed(await userManager.UpdateAsync(user));
        OperationLogWriter.Add(db, actor, "UpdateAccount", "Account", user.Id, user.UserName!, null, "/账户管理", new { PreviousDisplayName = previousDisplayName, user.DisplayName, PreviousEnabled = previousEnabled, user.IsEnabled });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(user, await db.DirectoryPermissions.CountAsync(x => x.AccountId == id, cancellationToken));
    }

    public async Task ResetPasswordAsync(Guid id, ResetPasswordRequest request, AccountContext actor, CancellationToken cancellationToken)
    {
        if (id == actor.Id) throw new InvalidOperationException("请使用“修改自己的密码”并验证当前密码。");
        var user = await RequireAccountAsync(id, cancellationToken);
        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        ThrowIfFailed(await userManager.ResetPasswordAsync(user, token, request.Password));
        OperationLogWriter.Add(db, actor, "ResetPassword", "Account", user.Id, user.UserName!, null, "/账户管理", new { user.UserName });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AccountPermissionsDto> GetPermissionsAsync(Guid id, CancellationToken cancellationToken)
    {
        _ = await RequireAccountAsync(id, cancellationToken);
        var permissions = await db.DirectoryPermissions.AsNoTracking().Where(x => x.AccountId == id)
            .OrderBy(x => x.FolderId)
            .Select(x => new DirectoryPermissionDto(x.FolderId, x.CanView, x.CanOperate))
            .ToListAsync(cancellationToken);
        return new AccountPermissionsDto(id, permissions);
    }

    public async Task<AccountPermissionsDto> ReplacePermissionsAsync(Guid id, ReplaceDirectoryPermissionsRequest request, AccountContext actor, CancellationToken cancellationToken)
    {
        var user = await RequireAccountAsync(id, cancellationToken);
        if (user.IsMasterAdmin) throw new InvalidOperationException("主账号默认拥有全部目录权限，无需单独配置。");

        var normalized = request.Permissions
            .Where(x => x.CanView || x.CanOperate)
            .GroupBy(x => x.FolderId)
            .Select(x => new DirectoryPermissionDto(x.Key, x.Any(y => y.CanView || y.CanOperate), x.Any(y => y.CanOperate)))
            .ToList();
        var folderIds = normalized.Where(x => x.FolderId.HasValue).Select(x => x.FolderId!.Value).ToHashSet();
        var existingFolderCount = await db.Folders.CountAsync(x => folderIds.Contains(x.Id), cancellationToken);
        if (existingFolderCount != folderIds.Count) throw new ArgumentException("权限配置中包含不存在的目录。");

        var existing = await db.DirectoryPermissions.Where(x => x.AccountId == id).ToListAsync(cancellationToken);
        db.DirectoryPermissions.RemoveRange(existing);
        var now = DateTimeOffset.UtcNow;
        foreach (var permission in normalized)
        {
            db.DirectoryPermissions.Add(new DirectoryPermission
            {
                Id = Guid.NewGuid(),
                AccountId = id,
                FolderId = permission.FolderId,
                CanView = permission.CanView,
                CanOperate = permission.CanOperate,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        OperationLogWriter.Add(db, actor, "ConfigurePermissions", "Account", user.Id, user.UserName!, null, "/账户管理", new { user.UserName, Permissions = normalized });
        await db.SaveChangesAsync(cancellationToken);
        return new AccountPermissionsDto(id, normalized);
    }

    private ApplicationUser CreateUser(string userName, string displayName, bool masterAdmin, Guid? createdBy)
    {
        var now = DateTimeOffset.UtcNow;
        return new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = NormalizeUserName(userName),
            DisplayName = NormalizeDisplayName(displayName),
            IsMasterAdmin = masterAdmin,
            IsEnabled = true,
            CreatedByAccountId = createdBy,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private async Task<ApplicationUser> RequireAccountAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new KeyNotFoundException("账号不存在。");

    private static string NormalizeUserName(string value)
    {
        var result = value.Trim();
        if (result.Length is < 3 or > 50) throw new ArgumentException("账号名称长度应为 3 到 50 个字符。");
        if (result.Any(char.IsWhiteSpace) || result.Any(char.IsControl)) throw new ArgumentException("账号名称不能包含空白或控制字符。");
        return result;
    }

    private static string NormalizeDisplayName(string value)
    {
        var result = value.Trim();
        if (result.Length is < 1 or > 50) throw new ArgumentException("显示名称长度应为 1 到 50 个字符。");
        return result;
    }

    private static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded) return;
        var messages = result.Errors.Select(x => x.Code switch
        {
            "DuplicateUserName" => "账号名称已存在。",
            "PasswordTooShort" => "密码至少需要 8 个字符。",
            "PasswordRequiresUniqueChars" => "密码中不同字符的数量不足。",
            "PasswordMismatch" => "当前密码不正确。",
            _ => x.Description
        });
        throw new ArgumentException(string.Join("；", messages));
    }

    private static AccountContext ToContext(ApplicationUser user) => new(user.Id, user.UserName!, user.DisplayName, user.IsMasterAdmin);
    private static AccountDto ToDto(ApplicationUser user, int permissionCount) => new(user.Id, user.UserName!, user.DisplayName, user.IsMasterAdmin, user.IsEnabled, permissionCount, user.CreatedAt, user.LastLoginAt);
}
