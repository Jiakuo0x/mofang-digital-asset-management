using Microsoft.EntityFrameworkCore;
using Mofang.Application;
using Mofang.Infrastructure.Persistence;

namespace Mofang.Infrastructure.Services;

public sealed class DirectoryAccessService(MofangDbContext db) : IDirectoryAccessService
{
    private Guid? _accountId;
    private FolderAccessSnapshot? _snapshot;

    public async Task<FolderAccessSnapshot> GetAccessAsync(AccountContext account, CancellationToken cancellationToken)
    {
        if (_snapshot is not null && _accountId == account.Id) return _snapshot;

        var folders = await db.Folders.AsNoTracking()
            .Select(x => new FolderAccessNode(x.Id, x.ParentId))
            .ToListAsync(cancellationToken);
        IReadOnlyCollection<DirectoryGrant> grants = account.IsMasterAdmin
            ? []
            : await db.DirectoryPermissions.AsNoTracking()
                .Where(x => x.AccountId == account.Id)
                .Select(x => new DirectoryGrant(x.FolderId, x.CanView, x.CanOperate))
                .ToListAsync(cancellationToken);

        _accountId = account.Id;
        _snapshot = DirectoryAccessEvaluator.Evaluate(account.IsMasterAdmin, folders, grants);
        return _snapshot;
    }

    public async Task EnsureCanViewAsync(AccountContext account, Guid? folderId, CancellationToken cancellationToken)
    {
        var access = await GetAccessAsync(account, cancellationToken);
        var allowed = folderId.HasValue
            ? access.Folders.TryGetValue(folderId.Value, out var folder) && folder.CanView
            : access.CanViewRoot;
        if (!allowed) throw new UnauthorizedAccessException("当前账号没有该目录的查看权限。");
    }

    public async Task EnsureCanOperateAsync(AccountContext account, Guid? folderId, CancellationToken cancellationToken)
    {
        var access = await GetAccessAsync(account, cancellationToken);
        var allowed = folderId.HasValue
            ? access.Folders.TryGetValue(folderId.Value, out var folder) && folder.CanOperate
            : access.CanOperateRoot;
        if (!allowed) throw new UnauthorizedAccessException("当前账号没有该目录的操作权限。");
    }
}
