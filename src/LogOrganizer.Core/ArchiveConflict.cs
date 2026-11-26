namespace LogOrganizer.Core;

public enum ConflictAction
{
    Overwrite,
    Rename,
    Skip,
}

/// <summary>An explicit user decision about a destination name collision.</summary>
public readonly record struct ConflictDecision
{
    private ConflictDecision(ConflictAction action, string? newName)
    {
        Action = action;
        NewName = newName;
    }

    public ConflictAction Action { get; }

    public string? NewName { get; }

    public static ConflictDecision Overwrite { get; } = new(ConflictAction.Overwrite, null);

    public static ConflictDecision Skip { get; } = new(ConflictAction.Skip, null);

    public static ConflictDecision Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            throw new ArgumentException("A rename needs a new name.", nameof(newName));
        }

        return new ConflictDecision(ConflictAction.Rename, newName);
    }
}

/// <summary>Describes a collision between a source item and an existing destination entry.</summary>
public sealed record ConflictRequest(string SourcePath, string ExistingPath, string SuggestedName);

public delegate ConflictDecision ConflictResolver(ConflictRequest request);

public enum ArchiveItemStatus
{
    Archived,
    Overwritten,
    Renamed,
    Skipped,
    Failed,
}

public sealed record ArchiveItemResult(
    string SourcePath,
    ArchiveItemStatus Status,
    string? DestinationPath,
    string? Error);

public sealed record ArchiveResult(string? TicketFolder, IReadOnlyList<ArchiveItemResult> Items, string? Error)
{
    public bool Succeeded => Error is null;

    public int MovedCount => Items.Count(static item =>
        item.Status is ArchiveItemStatus.Archived
            or ArchiveItemStatus.Overwritten
            or ArchiveItemStatus.Renamed);

    public int SkippedCount => Items.Count(static item => item.Status == ArchiveItemStatus.Skipped);

    public static ArchiveResult Failure(string error) =>
        new(null, Array.Empty<ArchiveItemResult>(), error);
}