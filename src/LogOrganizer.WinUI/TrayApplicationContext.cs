using System.Diagnostics;
using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using LogOrganizer.Core;
using WinRT.Interop;

namespace LogOrganizer.WinUI;

internal sealed class TrayApplicationContext : IDisposable
{
    internal const string MutexName = @"Global\LogOrganizer.SingleInstance.v1";

    private readonly App _app;
    private readonly SettingsStore _store = new();
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly Dictionary<string, string> _itemErrors = new(StringComparer.OrdinalIgnoreCase);
    private InboxWatcher? _watcher;
    private MainWindow? _window;
    private GlobalHotkey? _hotkey;
    private AppSettings _settings;
    private bool _disposed;
    private bool _savePending;
    private volatile bool _primingWatcher;
    private int _lastPendingCount;
    private long _lastFlashTicks;

    public TrayApplicationContext(App app)
    {
        _app = app;
        _settings = _store.Load();

        // PanelPinned is live state that is never persisted, so every launch starts from the saved
        // preference. From here on the title-bar pin owns it and the preference is left alone.
        _settings.PanelPinned = _settings.StartPinned;

        var queue = DispatcherQueue.GetForCurrentThread();

        // Dragging a window edge raises DidSizeChange dozens of times a second; write once it settles.
        _saveTimer = queue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(700);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += OnSaveTick;
    }

    public AppSettings Settings => _settings;
    public bool IsPinned => _settings.PanelPinned;
    public bool HasTarget => _settings.HasTarget && Directory.Exists(_settings.TargetRoot);

    public event EventHandler? PendingChanged;

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

        var hotkeyError = ApplyHotkey();
        if (hotkeyError is not null)
        {
            _window.ShowStatusMessage(hotkeyError, isError: true);
        }
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

    /// <summary>
    /// Records the live pin state. Deliberately does not save settings: the preference for the next
    /// launch is "启动时置顶" and is owned by the settings window, so pinning here must not rewrite it.
    /// </summary>
    public void SetPinned(bool pinned)
    {
        if (_settings.PanelPinned == pinned)
        {
            return;
        }

        _settings.PanelPinned = pinned;
        _window?.RefreshPending();
    }

    public void SaveWindowPosition(int left, int top)
    {
        _settings.PanelLeft = left;
        _settings.PanelTop = top;
        QueueSave();
    }

    public void SaveWindowSize(int width, int height)
    {
        _settings.PanelWidth = width;
        _settings.PanelHeight = height;
        QueueSave();
    }

    /// <summary>
    /// Applies the edited settings and returns the shortcut registration error, if the newly chosen
    /// combination turned out to be taken between the probe and the save.
    /// </summary>
    public string? ApplySettings(AppSettings updated)
    {
        _settings = SettingsStore.Normalize(updated);
        _itemErrors.Clear();
        StartWatcher();
        ApplyStartupRegistration();
        SaveSettings();
        return ApplyHotkey();
    }

    /// <summary>
    /// Checks whether a combination can be registered right now, without losing the active shortcut.
    /// RegisterHotKey is exclusive, so the only honest test is to try it and give it back.
    /// </summary>
    public string? ProbeHotkey(HotkeyGesture gesture)
    {
        if (_hotkey is not null && _hotkey.Gesture == gesture)
        {
            return null;
        }

        _hotkey?.Dispose();
        _hotkey = null;
        var probe = GlobalHotkey.TryCreate(gesture, static () => { }, out var error);
        probe?.Dispose();
        ApplyHotkey();
        return error;
    }

    /// <summary>Registers the configured shortcut, or returns why it could not be registered.</summary>
    private string? ApplyHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        if (!_settings.WakeHotkeyEnabled)
        {
            return null;
        }

        if (!HotkeyGesture.TryParse(_settings.WakeHotkey, out var gesture))
        {
            gesture = HotkeyGesture.Default;
        }

        _hotkey = GlobalHotkey.TryCreate(gesture, OnHotkeyPressed, out var error);
        return _hotkey is null ? error : null;
    }

    private void OnHotkeyPressed() => _window?.FocusTicketBox();

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
        NativeMethods.ForceForeground(WindowNative.GetWindowHandle(_window));
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
        InboxWatcher? previous = _watcher;
        try
        {
            var watcher = new InboxWatcher(_settings.InboxPath);
            watcher.PendingChanged += OnWatcherPendingChanged;
            watcher.Failed += (_, ex) => NotifyWatcherFailure(ex);

            // The initial rescan reports whatever is already in the inbox. That is a state, not
            // something new that just arrived, so it must not flash the taskbar.
            _primingWatcher = true;
            try
            {
                watcher.Start();
            }
            finally
            {
                _primingWatcher = false;
            }

            // Swap first, dispose second: tearing the old watcher down while the window is visible
            // used to hide it, so saving settings with the panel open made the panel disappear.
            _watcher = watcher;
            _lastPendingCount = watcher.SnapshotPending().Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _watcher = null;
            _window?.ShowStatusMessage($"无法监控中转目录：{ex.Message}", isError: true);
        }
        finally
        {
            previous?.Dispose();
        }
    }

    private void OnWatcherPendingChanged(object? sender, EventArgs e)
    {
        PendingChanged?.Invoke(this, EventArgs.Empty);

        var count = _watcher?.SnapshotPending().Count ?? 0;
        var grew = count > _lastPendingCount;
        _lastPendingCount = count;
        if (grew && !_primingWatcher)
        {
            FlashForNewItems(count);
        }
    }

    /// <summary>
    /// Reminds the user that something new is waiting without taking focus away from the program
    /// they are working in: the taskbar button flashes and the window stays where it is.
    /// </summary>
    private void FlashForNewItems(int count)
    {
        if (_disposed || _window is null)
        {
            return;
        }

        // This runs on a watcher thread, and every Window call has to be made on the UI thread.
        var window = _window;
        window.DispatcherQueue.TryEnqueue(() =>
        {
            if (_disposed)
            {
                return;
            }

            // A copy raises Created plus a burst of Changed events; coalesce the lot into one flash.
            var now = Environment.TickCount64;
            if (now - _lastFlashTicks < 2000)
            {
                return;
            }

            _lastFlashTicks = now;
            if (!window.Visible)
            {
                // A hidden window has no taskbar button to flash; show it without activating.
                window.AppWindow.Show(activateWindow: false);
            }

            NativeMethods.FlashTaskbar(WindowNative.GetWindowHandle(window));
            window.ShowNewItemsNotice(count);
        });
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
        _savePending = false;
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

    private void QueueSave()
    {
        _savePending = true;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTick(DispatcherQueueTimer sender, object args) => SaveSettings();

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
        _saveTimer.Stop();
        _saveTimer.Tick -= OnSaveTick;
        if (_savePending)
        {
            // Flush the last drag/resize instead of losing it because the app is exiting.
            SaveSettings();
        }

        _hotkey?.Dispose();
        _hotkey = null;
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
