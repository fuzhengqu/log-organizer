namespace LogOrganizer.Core;

/// <summary>
/// Observes the inbox and tracks the pending top-level files and folders. Entries appear as soon
/// as they exist; there is no quiet period, because archiving only ever happens on an explicit
/// click. It never mutates the inbox; archiving owns all file moves.
/// </summary>
public sealed class InboxWatcher : IDisposable
{
    /// <summary>
    /// Partial download and editor lock files are not finished yet, so they are never offered for
    /// archiving. This is a name filter, not a timer: a normal file shows up the moment it lands.
    /// </summary>
    private static readonly string[] IgnoredSuffixes =
    {
        ".crdownload",
        ".part",
        ".partial",
        ".download",
        ".opdownload",
        ".!ut",
        ".tmp",
    };

    private readonly object _gate = new();
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    public InboxWatcher(string inboxPath)
    {
        if (string.IsNullOrWhiteSpace(inboxPath))
        {
            throw new ArgumentException("Inbox path is required.", nameof(inboxPath));
        }

        InboxPath = Path.GetFullPath(inboxPath);

        // The inbox is created on first run; FileSystemWatcher refuses to attach to a
        // missing directory, so the folder must exist before the watcher is constructed.
        Directory.CreateDirectory(InboxPath);

        _watcher = new FileSystemWatcher(InboxPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime,
            InternalBufferSize = 64 * 1024,
        };

        _watcher.Created += OnCreatedOrChanged;
        _watcher.Changed += OnCreatedOrChanged;
        _watcher.Renamed += OnRenamed;
        _watcher.Deleted += OnDeleted;
        _watcher.Error += OnError;
    }

    public string InboxPath { get; }

    public bool IsRunning => _watcher.EnableRaisingEvents;

    /// <summary>Raised when the pending set changes. Raised on a watcher thread.</summary>
    public event EventHandler? PendingChanged;

    /// <summary>Raised when the underlying watcher reports an error, for example a buffer overflow.</summary>
    public event EventHandler<Exception>? Failed;

    public void Start()
    {
        Directory.CreateDirectory(InboxPath);
        _watcher.EnableRaisingEvents = true;
        Rescan();
    }

    /// <summary>Re-reads the inbox so pre-existing or missed entries become pending.</summary>
    public void Rescan()
    {
        if (!Directory.Exists(InboxPath))
        {
            return;
        }

        var added = false;
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(InboxPath))
            {
                if (IsIgnored(entry))
                {
                    continue;
                }

                lock (_gate)
                {
                    added |= _pending.Add(Path.GetFullPath(entry));
                }
            }
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        if (added)
        {
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<string> SnapshotPending()
    {
        lock (_gate)
        {
            return _pending
                .Where(static path => File.Exists(path) || Directory.Exists(path))
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    /// <summary>Tracks a path that did not come from the inbox, for example a dropped file.</summary>
    public void AddPending(string path)
    {
        if (_disposed || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return;
        }

        bool added;
        lock (_gate)
        {
            added = _pending.Add(fullPath);
        }

        if (added)
        {
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Removes entries that were archived, skipped by the user, or no longer exist.</summary>
    public void RemovePending(IEnumerable<string> paths)
    {
        var removed = false;
        lock (_gate)
        {
            foreach (var path in paths)
            {
                removed |= _pending.Remove(Path.GetFullPath(path));
            }
        }

        if (removed)
        {
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PruneMissing()
    {
        var stale = SnapshotPending()
            .Where(static path => !File.Exists(path) && !Directory.Exists(path))
            .ToArray();

        if (stale.Length > 0)
        {
            RemovePending(stale);
        }
    }

    private void OnCreatedOrChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var topLevel = GetTopLevelEntry(e.FullPath);
        if (topLevel is null || IsIgnored(topLevel))
        {
            return;
        }

        var added = false;
        lock (_gate)
        {
            added = _pending.Add(topLevel);
        }

        if (added)
        {
            PendingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        OnCreatedOrChanged(sender, e);

        var previous = GetTopLevelEntry(e.OldFullPath);
        if (previous is not null && !File.Exists(previous) && !Directory.Exists(previous))
        {
            RemovePending(new[] { previous });
        }
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        var relative = GetRelativePath(e.FullPath);
        if (relative is null || relative.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return;
        }

        RemovePending(new[] { Path.GetFullPath(e.FullPath) });
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Rescan();
        Failed?.Invoke(this, e.GetException());
    }

    private string? GetTopLevelEntry(string fullPath)
    {
        var relative = GetRelativePath(fullPath);
        if (relative is null)
        {
            return null;
        }

        var separatorIndex = relative.IndexOf(Path.DirectorySeparatorChar);
        var name = separatorIndex < 0 ? relative : relative[..separatorIndex];
        if (name.Length == 0)
        {
            return null;
        }

        var candidate = Path.Combine(InboxPath, name);
        return File.Exists(candidate) || Directory.Exists(candidate) ? candidate : null;
    }

    private string? GetRelativePath(string fullPath)
    {
        string full;
        try
        {
            full = Path.GetFullPath(fullPath);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var prefix = InboxPath.EndsWith(Path.DirectorySeparatorChar)
            ? InboxPath
            : InboxPath + Path.DirectorySeparatorChar;

        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var relative = full[prefix.Length..];
        return relative.Length == 0 ? null : relative;
    }

    private static bool IsIgnored(string path)
    {
        var name = Path.GetFileName(path);

        if (name.Length == 0 || name.StartsWith('~') || name.StartsWith('.'))
        {
            return true;
        }

        foreach (var suffix in IgnoredSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnCreatedOrChanged;
        _watcher.Changed -= OnCreatedOrChanged;
        _watcher.Renamed -= OnRenamed;
        _watcher.Deleted -= OnDeleted;
        _watcher.Error -= OnError;
        _watcher.Dispose();
    }
}
