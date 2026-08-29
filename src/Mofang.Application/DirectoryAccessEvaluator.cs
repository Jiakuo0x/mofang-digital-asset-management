namespace Mofang.Application;

public static class DirectoryAccessEvaluator
{
    public static FolderAccessSnapshot Evaluate(
        bool isMasterAdmin,
        IReadOnlyCollection<FolderAccessNode> folders,
        IReadOnlyCollection<DirectoryGrant> grants)
    {
        if (isMasterAdmin)
        {
            return new FolderAccessSnapshot(
                true,
                true,
                folders.ToDictionary(x => x.Id, _ => new FolderAccess(true, true)));
        }

        var rootView = grants.Any(x => x.FolderId is null && x.CanView);
        var rootOperate = grants.Any(x => x.FolderId is null && x.CanOperate);
        var byId = folders.ToDictionary(x => x.Id);
        var children = folders.Where(x => x.ParentId.HasValue).GroupBy(x => x.ParentId!.Value).ToDictionary(x => x.Key, x => x.Select(y => y.Id).ToArray());
        var view = new HashSet<Guid>();
        var operate = new HashSet<Guid>();

        if (rootView) foreach (var folder in folders) view.Add(folder.Id);
        if (rootOperate) foreach (var folder in folders) operate.Add(folder.Id);

        foreach (var grant in grants.Where(x => x.FolderId.HasValue && (x.CanView || x.CanOperate)))
        {
            if (!byId.ContainsKey(grant.FolderId!.Value)) continue;
            var queue = new Queue<Guid>();
            queue.Enqueue(grant.FolderId.Value);
            while (queue.TryDequeue(out var id))
            {
                if (grant.CanView || grant.CanOperate) view.Add(id);
                if (grant.CanOperate) operate.Add(id);
                if (children.TryGetValue(id, out var descendants))
                    foreach (var child in descendants) queue.Enqueue(child);
            }
        }

        var navigation = new HashSet<Guid>(view);
        foreach (var id in view)
        {
            var parentId = byId[id].ParentId;
            while (parentId.HasValue && byId.TryGetValue(parentId.Value, out var parent))
            {
                navigation.Add(parent.Id);
                parentId = parent.ParentId;
            }
        }

        return new FolderAccessSnapshot(
            rootView,
            rootOperate,
            navigation.ToDictionary(
                id => id,
                id => new FolderAccess(view.Contains(id), operate.Contains(id), !view.Contains(id))));
    }
}
