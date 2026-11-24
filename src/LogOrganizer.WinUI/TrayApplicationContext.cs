using System.Diagnostics;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using LogOrganizer.Core;

namespace LogOrganizer.WinUI;

internal sealed class TrayApplicationContext : IDisposable
{
    internal const string MutexName = @"Global\LogOrganizer.SingleInstance.v1";

    private readonly App _app;
    private readonly SettingsStore _store = new();
    private readonly DispatcherQueueTimer _pollTimer;
    private readonly Dictionary<string, string> _itemErrors = new(StringComparer.OrdinalIgnoreCase);
    private InboxWatcher? _watcher;
    private MainWindow? _window;
    private AppSettings _settings;
    private bool _disposed;
    private bool _paused;

    public TrayApplicationContext(App app)
    {
        _app = app;
        _settings = _store.Load();
        _pollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromMilliseconds(250);
        _pollTimer.Tick += OnPollTick;
    }

    public AppSettings Settings => _settings;
    public bool IsPinned => _settings.PanelPinned;
    public bool IsPaused => _paused;
    public bool HasTarget => _settings.HasTarget && Directory.Exists(_settings.TargetRoot);

    public event EventHandler? PendingChanged;
    public event EventHandler? QuietActivity;

    public void Start()
    {
        StartWatcher();
        ApplyStartupRegistration();
        _window = new MainWindow(this);
        _window.Activate();
        if (IsPinned)
        {
            ShowWindow();
        }

        _pollTimer.Start();
    }

    public IReadOnlyList<PendingItem> GetPendingItems() => _watcher?.SnapshotPending()
        .Select(path => new PendingItem(path, Path.GetFileName(path), _itemErrors.GetValueOrDefault(path)))
        .ToArray() ?? Array.Empty<PendingItem>();

    public void AddExternalPath(string path)
    {
        _watcher?.AddPending(path);
        ShowWindow();
    }

    public void SetPendingError(string path, string error) => _itemErrors[path] = error;

    public void RemovePending(string path)
    {
        _itemErrors.Remove(path);
        _watcher?.RemovePending(new[] { path });
    }

    public async Task<ArchiveResult> ArchiveItemsAsync(IReadOnlyList<PendingItem> items, string ticket)
    {
        if (string.IsNullOrWhiteSpace(_settings.TargetRoot))
        {
            return ArchiveResult.Failure("尚未设置目标目录，请先在设置中配置。");
        }

        var drops = items.Select(static item => new ArchiveDropItem(item.SourcePath, item.DisplayName)).ToArray();
        return await ArchiveService.ArchiveDroppedAsync(_settings.TargetRoot, ticket, drops, ResolveConflict);
    }

    public void SetPinned(bool pinned)
    {
        if (_paused)
        {
            return;
        }

        if (_settings.PanelPinned == pinned)
        {
            return;
        }

        _settings.PanelPinned = pinned;
        if (pinned)
        {
            _watcher?.ResetQuietPeriod();
        }
        else
        {
            _watcher?.ResumeQuietPeriod();
        }

        SaveSettings();
        _window?.RefreshPending();
        _window?.RefreshTrayMenu();
    }

    public void SaveWindowPosition(int left, int top)
    {
        _settings.PanelLeft = left;
        _settings.PanelTop = top;
        SaveSettings();
    }

    public void ApplySettings(AppSettings updated)
    {
        _settings = SettingsStore.Normalize(updated);
        _paused = false;
        _itemErrors.Clear();
        StartWatcher();
        ApplyStartupRegistration();
        SaveSettings();
    }

    public void TogglePause()
    {
        _paused = !_paused;
        if (_paused)
        {
            _watcher?.Pause();
        }
        else
        {
            _watcher?.Resume();
            if (IsPinned)
            {
                _watcher?.ResetQuietPeriod();
            }
        }

        _window?.RefreshPending();
        _window?.RefreshTrayMenu();
    }

    public void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            _window?.ShowStatusMessage("文件夹不存在或尚未设置。", isError: true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            _window?.ShowStatusMessage($"无法打开文件夹：{ex.Message}", isError: true);
        }
    }

    public void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.AppWindow.Show();
        _window.Activate();
    }

    public void ToggleWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (IsPinned)
        {
            ShowWindow();
        }
        else if (_window.Visible)
        {
            _window.AppWindow.Hide();
        }
        else
        {
            ShowWindow();
        }
    }

    public void NotifyWatcherFailure(Exception exception)
    {
        _window?.ShowStatusMessage($"文件监控异常：{exception.Message}", isError: true);
    }

    private void StartWatcher()
    {
        _watcher?.Dispose();
        try
        {
            var watcher = new InboxWatcher(_settings.InboxPath, _settings.QuietPeriod);
            watcher.PendingChanged += OnWatcherPendingChanged;
            watcher.ActivityRecorded += OnWatcherActivityRecorded;
            watcher.Failed += (_, ex) => NotifyWatcherFailure(ex);
            watcher.Start(suspendQuietPeriod: _settings.PanelPinned);
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _watcher = null;
            _window?.ShowStatusMessage($"无法监控中转目录：{ex.Message}", isError: true);
        }
    }

    private void OnWatcherPendingChanged(object? sender, EventArgs e) => PendingChanged?.Invoke(this, EventArgs.Empty);
    private void OnWatcherActivityRecorded(object? sender, EventArgs e) => QuietActivity?.Invoke(this, EventArgs.Empty);

    private void OnPollTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || _paused || IsPinned || _watcher is null)
        {
            return;
        }

        if (_watcher.TryTakeReadyBatch(out _))
        {
            ShowWindow();
            _window?.ShowArchiveReady();
        }
    }

    private ConflictDecision ResolveConflict(ConflictRequest request) =>
        _window?.DispatchConflictDialog(request) ?? ConflictDecision.Skip;

    private void ApplyStartupRegistration()
    {
        if (!StartupRegistration.TryApply(_settings.StartWithWindows, out var error) && error is not null)
        {
            _window?.ShowStatusMessage($"开机自启设置失败：{error}", isError: true);
        }
    }

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Write(ex, "保存设置失败");
            _window?.ShowStatusMessage($"保存设置失败：{ex.Message}", isError: true);
        }
    }

    public void Shutdown()
    {
        Dispose();
        _app.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pollTimer.Stop();
        _pollTimer.Tick -= OnPollTick;
        _watcher?.Dispose();
        _watcher = null;
        _app.ReleaseSingleInstance();
        _window?.DisposeForShutdown();
        _window = null;
    }

    internal static void ShowAlreadyRunning() => NativeMethods.MessageBox(
        IntPtr.Zero, "日志归档助手已经在运行。", "日志归档助手", 0x40);

    internal static void ShowStartupError(Exception exception) => NativeMethods.MessageBox(
        IntPtr.Zero, $"启动失败：{exception.Message}", "日志归档助手", 0x10);
}
