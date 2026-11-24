namespace LogOrganizer.Core;

/// <summary>One item shown in the pending list: source path, display name and an optional per-item error.</summary>
public sealed record PendingItem(string SourcePath, string DisplayName, string? Error = null);