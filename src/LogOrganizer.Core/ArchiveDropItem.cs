namespace LogOrganizer.Core;

/// <summary>A source path and the safe, user-selected name to use in the ticket folder.</summary>
public sealed record ArchiveDropItem(string SourcePath, string TargetName);
