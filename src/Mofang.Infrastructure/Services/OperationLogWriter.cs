using System.Text.Json;
using Mofang.Application;
using Mofang.Domain;
using Mofang.Infrastructure.Persistence;

namespace Mofang.Infrastructure.Services;

internal static class OperationLogWriter
{
    public static void Add(
        MofangDbContext db,
        AccountContext actor,
        string action,
        string targetType,
        Guid targetId,
        string targetName,
        Guid? folderId,
        string folderPath,
        object detail,
        string? previousName = null,
        string? newName = null)
    {
        db.OperationLogs.Add(new OperationLog
        {
            Id = Guid.NewGuid(),
            AccountId = actor.Id,
            UserId = actor.UserName,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            TargetName = targetName,
            FolderId = folderId,
            FolderPath = folderPath,
            PreviousName = previousName,
            NewName = newName,
            Detail = JsonSerializer.Serialize(detail),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }
}
