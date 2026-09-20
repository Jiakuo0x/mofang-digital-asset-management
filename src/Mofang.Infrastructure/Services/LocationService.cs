using Microsoft.EntityFrameworkCore;
using Mofang.Application;
using Mofang.Contracts;
using Mofang.Domain;
using Mofang.Infrastructure.Persistence;

namespace Mofang.Infrastructure.Services;

public sealed class LocationService(MofangDbContext db, IDirectoryAccessService directoryAccess) : ILocationService
{
    private const string Unavailable = "文件或文件夹不存在，或你没有查看权限。";
    private const int Limit = 50;

    public async Task<LocationDto> GetAsync(string kind, Guid? id, AccountContext account, CancellationToken cancellationToken)
    {
        if (kind is not ("asset" or "folder") || (kind == "asset" && id is null))
            throw new ArgumentException("无效的位置类型。");
        var access = await directoryAccess.GetAccessAsync(account, cancellationToken);
        var libraryId = await LibraryIdAsync(cancellationToken);
        var folders = await db.Folders.AsNoTracking().ToDictionaryAsync(x => x.Id, cancellationToken);
        if (kind == "folder")
        {
            if (!CanView(access, id) || (id.HasValue && !folders.ContainsKey(id.Value))) throw new KeyNotFoundException(Unavailable);
            return FolderLocation(libraryId, id.HasValue ? folders[id.Value] : null, folders);
        }
        var asset = await db.Assets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (asset is null || !CanView(access, asset.FolderId)) throw new KeyNotFoundException(Unavailable);
        return AssetLocation(libraryId, asset, folders);
    }

    public async Task<ResolveLocationResponse> ResolveAsync(string input, AccountContext account, CancellationToken cancellationToken)
    {
        var parsed = LocationInput.Parse(input);
        var libraryId = await LibraryIdAsync(cancellationToken);
        if (parsed.LibraryId.HasValue)
        {
            if (parsed.LibraryId != libraryId) return new("differentLibrary", [], ExpectedLibraryId: parsed.LibraryId);
            return new("exact", [await GetAsync(parsed.Kind!, parsed.Id, account, cancellationToken)]);
        }

        var access = await directoryAccess.GetAccessAsync(account, cancellationToken);
        var folders = await db.Folders.AsNoTracking().ToDictionaryAsync(x => x.Id, cancellationToken);
        var visibleFolders = folders.Values.Where(x => x.Status == EntityStatus.Active && CanView(access, x.Id)).ToArray();
        var visibleIds = visibleFolders.Select(x => x.Id).ToArray();
        var assets = db.Assets.AsNoTracking().Where(x => x.Status == EntityStatus.Active &&
            ((x.FolderId == null && access.CanViewRoot) || (x.FolderId != null && visibleIds.Contains(x.FolderId.Value))));

        if (parsed.IsPath)
        {
            var exact = visibleFolders.Where(x => FolderPath(x.Id, folders) == parsed.Text)
                .Select(x => FolderLocation(libraryId, x, folders)).ToList();
            if (parsed.Text == "/" && access.CanViewRoot) exact.Add(FolderLocation(libraryId, null, folders));
            var split = parsed.Text.LastIndexOf('/');
            var parent = split == 0 ? "/" : parsed.Text[..split];
            var name = parsed.Text[(split + 1)..];
            var parentIds = visibleFolders.Where(x => FolderPath(x.Id, folders) == parent).Select(x => x.Id).ToArray();
            var exactAssets = await assets.Where(x => x.FileName == name &&
                ((parent == "/" && x.FolderId == null) || (x.FolderId != null && parentIds.Contains(x.FolderId.Value))))
                .OrderBy(x => x.Id).Take(Limit + 1).ToListAsync(cancellationToken);
            exact.AddRange(exactAssets.Select(x => AssetLocation(libraryId, x, folders)));
            if (exact.Count > 0) return new(exact.Count == 1 ? "exact" : "candidates", exact.Take(Limit).ToArray(), exact.Count > Limit);
        }

        // Only permission-filtered candidates are returned; a partial path never auto-navigates.
        var fragment = parsed.Text.Trim('/');
        if (fragment.Length == 0) return new("none", []);
        var matches = visibleFolders.Select(x => FolderLocation(libraryId, x, folders))
            .Where(x => x.Path.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Path, StringComparer.Ordinal).Take(Limit + 1).ToList();
        var slash = fragment.LastIndexOf('/');
        var term = (slash < 0 ? fragment : fragment[(slash + 1)..]).ToLowerInvariant();
        if (slash >= 0)
        {
            var directory = fragment[..slash];
            var matchingParents = visibleFolders.Where(x => FolderPath(x.Id, folders).Contains(directory, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToArray();
            assets = assets.Where(x => x.FolderId != null && matchingParents.Contains(x.FolderId.Value));
        }
        var candidates = await assets.Where(x => x.FileName.ToLower().Contains(term)).OrderBy(x => x.FileName).ThenBy(x => x.Id)
            .Take(Limit + 1).ToListAsync(cancellationToken);
        matches.AddRange(candidates.Select(x => AssetLocation(libraryId, x, folders)));
        return new(matches.Count == 0 ? "none" : "candidates", matches.Take(Limit).ToArray(), matches.Count > Limit);
    }

    private Task<Guid> LibraryIdAsync(CancellationToken cancellationToken) =>
        db.LibraryIdentities.Where(x => x.Id == 1).Select(x => x.LibraryId).SingleAsync(cancellationToken);

    private static bool CanView(FolderAccessSnapshot access, Guid? id) => id.HasValue
        ? access.Folders.TryGetValue(id.Value, out var permission) && permission.CanView : access.CanViewRoot;

    private static string FolderPath(Guid? id, IReadOnlyDictionary<Guid, Folder> folders)
    {
        var parts = new Stack<string>();
        var seen = new HashSet<Guid>();
        while (id.HasValue)
        {
            if (!seen.Add(id.Value) || !folders.TryGetValue(id.Value, out var folder)) throw new KeyNotFoundException(Unavailable);
            parts.Push(folder.Name);
            id = folder.ParentId;
        }
        return "/" + string.Join('/', parts);
    }

    private static LocationDto FolderLocation(Guid libraryId, Folder? folder, IReadOnlyDictionary<Guid, Folder> folders) =>
        new(libraryId, "folder", folder?.Id, folder?.Name ?? "资产库根目录", FolderPath(folder?.Id, folders),
            Code(libraryId, "folder", folder?.Id), (folder?.Status ?? EntityStatus.Active).ToString(), folder?.ParentId, null, folder?.UpdatedAt);

    private static LocationDto AssetLocation(Guid libraryId, Asset asset, IReadOnlyDictionary<Guid, Folder> folders) =>
        new(libraryId, "asset", asset.Id, asset.FileName, FolderPath(asset.FolderId, folders).TrimEnd('/') + "/" + asset.FileName,
            Code(libraryId, "asset", asset.Id), asset.Status.ToString(), asset.FolderId, asset.FileSize, asset.UpdatedAt);

    private static string Code(Guid libraryId, string kind, Guid? id) => $"MF1:{libraryId:D}:{kind}:{(id.HasValue ? id.Value.ToString("D") : "root")}";
}
