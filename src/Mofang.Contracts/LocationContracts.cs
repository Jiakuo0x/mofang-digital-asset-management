using System.ComponentModel.DataAnnotations;

namespace Mofang.Contracts;

public sealed record LocationDto(
    Guid LibraryId, string Kind, Guid? Id, string Name, string Path, string Code,
    string Status, Guid? FolderId, long? FileSize, DateTimeOffset? UpdatedAt);

public sealed record ResolveLocationRequest([Required, StringLength(8192)] string Input);

// Match: exact, candidates, none, differentLibrary. Candidate results never imply an exact match.
public sealed record ResolveLocationResponse(
    string Match, IReadOnlyList<LocationDto> Items, bool HasMore = false, Guid? ExpectedLibraryId = null);
