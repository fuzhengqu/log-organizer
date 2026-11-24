namespace LogOrganizer.Core;

/// <summary>
/// Observes the inbox and exposes the pending top-level files and folders once the
/// quiet period expires. It never mutates the inbox; archiving owns all file moves.
/// </summary>
public sealed class InboxWatcher : IDisposable
{
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
    private readonly QuietPeriod _quietPeriod;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _quietGate = new();
    private TimeSpan? _pausedQuietRemaining;
    private readonly FileSystemWatcher _watcher;
    private bool _disposed;

    public InboxWatcher(string inboxPath, TimeSpan quietPeriod, Func<DateTimeOffset>? clock = null)
    {
        if (string.IsNullOrWhiteSpace(inboxPath))
        {
            throw new ArgumentException("Inbox path is required.", nameof(inboxPath));
        }

        InboxPath = Path.GetFullPath(inboxPath);
        _quietPeriod = new QuietPeriod(quietPeriod);
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);

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

    public bool IsPaused { get; private set; }

    /// <summary>Raised when the pending set changes. Raised on a watcher thread.</summary>
    public event EventHandler? PendingChanged;

    /// <summary>Raised when non-ignored inbox activity restarts the quiet period.</summary>
    public event EventHandler? ActivityRecorded;

    /// <summary>Raised when the underlying watcher reports an error, for example a buffer overflow.</summary>
    public event EventHandler<Exception>? Failed;

    public void Start(bool suspendQuietPeriod = false)
    {
        Directory.CreateDirectory(InboxPath);
        IsPaused = false;
        _watcher.EnableRaisingEvents = true;
        Rescan();
        if (suspendQuietPeriod)
        {
            ResetQuietPeriod();
        }
    }

    public void Pause()
    {
        IsPaused = true;
        _watcher.EnableRaisingEvents = false;
    }

    public void Resume()
    {
        if (_disposed)
        {
            return;
        }

        IsPaused = false;
        Directory.CreateDirectory(InboxPath);
        _watcher.EnableRaisingEvents = true;
        Rescan();
    }

    /// <summary>Records activity now, restarting the quiet period.</summary>
    public void MarkActivity() => RecordQuietActivity();

    private void RecordQuietActivity()
    {
        ActivityRecorded?.Invoke(this, EventArgs.Empty);

        lock (_quietGate)
        {
            if (_pausedQuietRemaining is not null)
            {
                _pausedQuietRemaining = _quietPeriod.Delay;
            }
            else
            {
                _quietPeriod.RecordChange(_clock());
            }
        }
    }

    /// <summary>Freezes the current quiet period while the manual drop panel is active.</summary>
    public void ResetQuietPeriod()
    {
        lock (_quietGate)
        {
            if (_pausedQuietRemaining is not null)
            {
                return;
            }

            _pausedQuietRemaining = _quietPeriod.DueAt is { } due
                ? due - _clock()
                : TimeSpan.Zero;
            _quietPeriod.Reset();
        }
    }

    /// <summary>Resumes automatic prompting with the quiet period preserved across panel mode.</summary>
    public void ResumeQuietPeriod()
    {
        lock (_quietGate)
        {
            if (_pausedQuietRemaining is { } remaining)
            {
                _quietPeriod.RecordChange(_clock() - (_quietPeriod.Delay - remaining));
                _pausedQuietRemaining = null;
            }
        }
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

        RecordQuietActivity();

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

    /// <summary>
    /// Returns the pending batch once the quiet period elapsed. Each batch is emitted at
    /// most once; pending entries stay tracked until the caller removes them.
    /// </summary>
    public bool TryTakeReadyBatch(out IReadOnlyList<string> batch)
    {
        batch = Array.Empty<string>();

        if (_disposed || IsPaused || !_quietPeriod.IsDue(_clock()))
        {
            return false;
        }

        _quietPeriod.Reset();

        var items = SnapshotPending();
        if (items.Count == 0)
        {
            return false;
        }

        batch = items;
        return true;
    }

    /// <summary>Removes entries that were archived, skipped by the user, or no longer exist.</summary>
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
        if (_disposed || IsPaused)
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

        RecordQuietActivity();

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