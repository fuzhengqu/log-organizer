using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Windows.Input;
using LogOrganizer.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LogOrganizer.WinUI;

public sealed partial class MainWindow : Window
{
    /// <summary>Design floor for the panel; below this the footer buttons and list stop fitting.</summary>
    private const double MinWindowWidthDip = 420;
    private const double MinWindowHeightDip = 460;

    private readonly TrayApplicationContext _context;
    private bool _refreshing;
    private bool _busy;
    private bool _shutdown;
    private System.Drawing.Icon? _trayIcon;
    private TrayMenu? _trayMenu;
    private Windows.Graphics.RectInt32 _pinHitRect;
    private bool _pinHitRectValid;

    public ICommand TrayDoubleClickCommand { get; } = new DelegateCommand(() =>
        ((App)Application.Current).ToggleWindow());

    internal MainWindow(TrayApplicationContext context)
    {
        _context = context;
        InitializeComponent();

        WindowChrome.ResizeDip(this, 388, 516);
        AppWindow.Title = "日志归档助手";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsAlwaysOnTop = _context.IsPinned;
        presenter.IsResizable = true;
        presenter.IsMaximizable = true;
        presenter.IsMinimizable = true;
        ApplyMinimumSize(presenter);
        AppIcon.Apply(this);
        ApplyTrayIcon();
        ApplyTrayMenu();
        AppWindow.Changed += OnWindowChanged;
        AppWindow.Closing += OnWindowClosing;
        TitleBar.Loaded += OnLoaded;
        Closed += OnClosed;
        _context.PendingChanged += OnPendingChanged;
        AlwaysOnTopButton.IsChecked = _context.IsPinned;
        RefreshPending();
        UpdateStatus();
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            UpdateTitleBarLayout();
        }

        if (!Visible || (sender.Presenter as OverlappedPresenter)?.State != OverlappedPresenterState.Restored)
        {
            // A maximised window reports the virtual screen bounds, which must not become the saved size.
            return;
        }

        if (args.DidSizeChange)
        {
            _context.SaveWindowSize(sender.Size.Width, sender.Size.Height);
        }

        if (args.DidPositionChange && !_context.IsPinned)
        {
            _context.SaveWindowPosition(sender.Position.X, sender.Position.Y);
        }
    }

    /// <summary>
    /// OverlappedPresenter takes physical pixels, so the floor is scaled per monitor; without this the
    /// panel would shrink to 380/1.5 DIP on a 150% display.
    /// </summary>
    private void ApplyMinimumSize(OverlappedPresenter presenter)
    {
        var scale = NativeMethods.DpiScale(WindowNative.GetWindowHandle(this));
        presenter.PreferredMinimumWidth = (int)Math.Round(MinWindowWidthDip * scale);
        presenter.PreferredMinimumHeight = (int)Math.Round(MinWindowHeightDip * scale);
    }

    private void ApplyTrayIcon()
    {
        _trayIcon = AppIcon.CreateTrayIcon();
        if (_trayIcon is not null)
        {
            Tray.Icon = _trayIcon;
        }
    }

    /// <summary>
    /// The tray menu is a Win32 popup: H.NotifyIcon's default PopupMenu mode depends on a package we
    /// do not ship, so right click is handled here and every item is wired to a real action.
    /// </summary>
    private void ApplyTrayMenu()
    {
        _trayMenu = TrayMenu.TryCreate(
            _context.ToggleWindow,
            () => _ = OpenSettingsAsync(),
            () => _context.OpenFolder(_context.Settings.InboxPath),
            () => _context.OpenFolder(_context.Settings.TargetRoot),
            _context.Shutdown,
            out var error);
        if (_trayMenu is null)
        {
            ShowStatus(error ?? "托盘菜单不可用。", isError: true);
            return;
        }

        Tray.RightClickCommand = new DelegateCommand(ShowTrayMenu);
    }

    /// <summary>
    /// Runs synchronously because TrackPopupMenu is modal: it returns the chosen command, which is
    /// applied straight after the menu closes.
    /// </summary>
    private void ShowTrayMenu()
    {
        if (_trayMenu is null || !NativeMethods.GetCursorPos(out var point))
        {
            return;
        }

        _trayMenu.Show(Visible, _context.HasTarget, point.X, point.Y);
    }

    public void FocusTicketBox()
    {
        _context.ShowWindow();
        TicketBox.Focus(FocusState.Programmatic);
        TicketBox.SelectionStart = TicketBox.Text.Length;
    }

    public void RefreshPending()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            RefreshPendingCore();
        }
        else
        {
            DispatcherQueue.TryEnqueue(RefreshPendingCore);
        }
    }

    public void ShowStatusMessage(string message, bool isError) =>
        DispatcherQueue.TryEnqueue(() => ShowStatus(message, isError));

    /// <summary>Called when a new file lands in the inbox; the taskbar flash itself is native.</summary>
    public void ShowNewItemsNotice(int count) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_busy)
        {
            ShowStatus($"检测到 {count} 个新文件，输入单号后点击归档。", isError: false);
        }
    });

    public ConflictDecision DispatchConflictDialog(ConflictRequest request)
    {
        var completion = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                completion.TrySetResult(await ShowConflictDialogAsync(request));
            }
            catch (Exception ex)
            {
                CrashLog.Write(ex, "显示冲突处理对话框失败");
                completion.TrySetResult(ConflictDecision.Skip);
            }
        }))
        {
            return ConflictDecision.Skip;
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    private async Task<ConflictDecision> ShowConflictDialogAsync(ConflictRequest request)
    {
        var decision = ConflictDecision.Skip;
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock
        {
            Text = $"目标位置已存在同名项目：{Path.GetFileName(request.ExistingPath)}",
            TextWrapping = TextWrapping.Wrap,
        });
        stack.Children.Add(new TextBlock { Text = request.ExistingPath, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        var options = new RadioButtons
        {
            ItemsSource = new[] { "重命名（两份都保留）", "覆盖目标项目", "跳过并保留源文件" },
            SelectedIndex = 0,
        };
        var renamed = new TextBox { Text = request.SuggestedName, Header = "新名称" };
        stack.Children.Add(options);
        stack.Children.Add(renamed);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "存在同名项目",
            Content = stack,
            PrimaryButtonText = "确定",
            CloseButtonText = "跳过",
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (options.SelectedIndex == 0)
            {
                if (!PathSafety.IsSimpleName(renamed.Text.Trim()))
                {
                    args.Cancel = true;
                    renamed.Description = "名称无效：不能包含路径分隔符或文件名非法字符。";
                    return;
                }

                decision = ConflictDecision.Rename(renamed.Text.Trim());
            }
            else if (options.SelectedIndex == 1)
            {
                decision = ConflictDecision.Overwrite;
            }
            else
            {
                decision = ConflictDecision.Skip;
            }
        };

        await dialog.ShowAsync();
        return decision;
    }

    public void DisposeForShutdown()
    {
        _shutdown = true;
        Closed -= OnClosed;
        AppWindow.Closing -= OnWindowClosing;
        AppWindow.Changed -= OnWindowChanged;
        _context.PendingChanged -= OnPendingChanged;
        Close();
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdown)
        {
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _context.PendingChanged -= OnPendingChanged;
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (!_shutdown)
        {
            _context.Shutdown();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestorePanelBounds(_context.Settings);
        ApplyPinnedOpacity();
        ApplyBackdrop();
        UpdateTitleBarLayout();
        RootSurface.SizeChanged += OnRootSurfaceSizeChanged;
        // Start in the ticket box instead of leaving focus on the title-bar pin button.
        TicketBox.Focus(FocusState.Programmatic);
        if (TitleBar.XamlRoot is not null)
        {
            TitleBar.XamlRoot.Changed += OnXamlRootChanged;
        }

        RootSurface.ActualThemeChanged += OnActualThemeChanged;
        RefreshPendingCore();
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateTitleBarLayout();

    private void OnRootSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => ReapplyPinHitTest();

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyBackdrop();
        UpdateTitleBarLayout();
    }

    /// <summary>
    /// Docks the pin button against the first system caption button and keeps header text clear of it.
    /// The right inset is only known once the window is extended into the title bar, so this runs on
    /// every layout-affecting change.
    /// </summary>
    private void UpdateTitleBarLayout()
    {
        if (!ExtendsContentIntoTitleBar || TitleBar.XamlRoot is null)
        {
            return;
        }

        var inset = WindowChrome.CaptionInsetDip(this, TitleBar.XamlRoot);
        AlwaysOnTopButton.Margin = new Thickness(0, 0, inset, 0);
        TitleBarHeader.Padding = new Thickness(
            20, 14, inset + WindowChrome.CaptionButtonWidthDip + 8, 14);
        ReapplyPinHitTest();
    }

    /// <summary>
    /// The pin lives inside the non-client title bar, so WinUI has to publish a passthrough region
    /// for it. That bookkeeping is recomputed on window and layout changes and the pin silently
    /// degrades to plain caption area when it is dropped: clicks do nothing and a small drag moves
    /// the window. Re-asserting the region ourselves keeps the button clickable.
    /// </summary>
    private void ReapplyPinHitTest()
    {
        if (!ExtendsContentIntoTitleBar || RootSurface.XamlRoot is null || AlwaysOnTopButton.ActualWidth <= 0)
        {
            return;
        }

        var scale = RootSurface.XamlRoot.RasterizationScale;
        var bounds = AlwaysOnTopButton
            .TransformToVisual(null)
            .TransformBounds(new Windows.Foundation.Rect(0, 0, AlwaysOnTopButton.ActualWidth, AlwaysOnTopButton.ActualHeight));
        var rect = new Windows.Graphics.RectInt32(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round(bounds.Width * scale),
            (int)Math.Round(bounds.Height * scale));

        if (_pinHitRectValid && rect.Equals(_pinHitRect))
        {
            return;
        }

        _pinHitRect = rect;
        _pinHitRectValid = true;
        Microsoft.UI.Input.InputNonClientPointerSource
            .GetForWindowId(AppWindow.Id)
            .SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, new[] { rect });
    }

    /// <summary>
    /// Reapplies the last size and position. Both are clamped to the current work area so a monitor
    /// change cannot leave the panel off-screen or taller than the display.
    /// </summary>
    private void RestorePanelBounds(AppSettings saved)
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (display is null)
        {
            return;
        }

        var area = display.WorkArea;
        if (saved.PanelWidth > 0 && saved.PanelHeight > 0)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                Math.Min(saved.PanelWidth, area.Width),
                Math.Min(saved.PanelHeight, area.Height)));
        }

        var size = AppWindow.Size;
        var x = saved.PanelLeft >= 0 ? saved.PanelLeft : area.X + ((area.Width - size.Width) / 2);
        var y = saved.PanelTop >= 0 ? saved.PanelTop : area.Y + ((area.Height - size.Height) / 2);
        AppWindow.Move(new Windows.Graphics.PointInt32(
            Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - size.Width)),
            Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height))));
    }

    private void OnPendingChanged(object? sender, EventArgs e) => RefreshPending();

    private void RefreshPendingCore()
    {
        if (_refreshing || _busy)
        {
            return;
        }

        _refreshing = true;
        try
        {
            var items = _context.GetPendingItems();
            ItemsList.ItemsSource = null;
            ItemsList.ItemsSource = items;
            EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PendingTitle.Text = $"待归档项目 · {items.Count}";
            PendingSubtitle.Text = _context.IsPinned
                ? "置顶模式 · 确认归档前不会移动文件"
                : "点击归档才会移动文件";
            ArchiveButton.IsEnabled = items.Count > 0 && TicketNumber.TryParse(TicketBox.Text.Trim(), out _)
                && _context.HasTarget;
            UpdateStatus();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void OnRemoveItem(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            _context.RemovePending(path);
            RefreshPendingCore();
        }
    }

    private void OnTicketBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs args)
    {
        args.Cancel = args.NewText.Any(static ch => ch is not (>= '0' and <= '9'));
    }

    private void OnTicketTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_refreshing)
        {
            RefreshPendingCore();
        }
    }

    private async void OnAddFiles(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.Downloads,
                ViewMode = PickerViewMode.List,
            };
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add("*");
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                _context.AddExternalPath(file.Path);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"无法打开文件选择器：{ex.Message}", isError: true);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        e.DragUIOverride.Caption = "添加到待归档列表";
        e.DragUIOverride.IsGlyphVisible = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            foreach (var item in items)
            {
                _context.AddExternalPath(item.Path);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"读取拖入项目失败：{ex.Message}", isError: true);
        }
    }

    private async void OnArchive(object sender, RoutedEventArgs e)
    {
        if (_busy || !TicketNumber.TryParse(TicketBox.Text.Trim(), out var ticket))
        {
            ShowStatus("问题单号只能包含 ASCII 数字，且不能为空。", isError: true);
            TicketBox.Focus(FocusState.Programmatic);
            return;
        }

        var items = _context.GetPendingItems();
        if (items.Count == 0)
        {
            return;
        }

        _busy = true;
        ArchiveButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        StatusText.Text = "正在安全归档…";
        try
        {
            var result = await _context.ArchiveItemsAsync(items, ticket);
            foreach (var item in result.Items.Where(static item => item.Status is ArchiveItemStatus.Archived
                         or ArchiveItemStatus.Overwritten or ArchiveItemStatus.Renamed))
            {
                _context.RemovePending(item.SourcePath);
            }

            foreach (var item in items.Where(item => result.Items.All(result => !string.Equals(
                         result.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase))))
            {
                if (!File.Exists(item.SourcePath) && !Directory.Exists(item.SourcePath))
                {
                    _context.RemovePending(item.SourcePath);
                }
            }

            foreach (var failed in result.Items.Where(static item => item.Status is ArchiveItemStatus.Failed or ArchiveItemStatus.Skipped))
            {
                _context.SetPendingError(failed.SourcePath, failed.Error ?? "已跳过");
            }

            if (result.Error is not null && result.Items.Count > 0)
            {
                foreach (var item in result.Items.Where(static item => item.Status == ArchiveItemStatus.Failed))
                {
                    _context.SetPendingError(item.SourcePath, $"{item.Error}（{result.Error}）");
                }
            }

            var errors = result.Items.Where(static item => item.Status is ArchiveItemStatus.Failed or ArchiveItemStatus.Skipped)
                .Select(item => $"{Path.GetFileName(item.SourcePath)}：{item.Error ?? "已跳过"}")
                .ToArray();
            if (result.Error is not null && errors.Length == 0)
            {
                errors = new[] { result.Error };
            }

            TicketBox.Text = string.Empty;
            if (errors.Length > 0)
            {
                ShowStatus(string.Join("；", errors.Take(2)), isError: true);
            }
            else if (result.Items.Count > 0)
            {
                ShowStatus($"已归档 {result.MovedCount} 项到 {ticket}，列表已清空。", isError: false);
            }

            RefreshPendingCore();
        }
        catch (Exception ex)
        {
            ShowStatus(DescribeArchiveFailure(ex), isError: true);
        }
        finally
        {
            _busy = false;
            SettingsButton.IsEnabled = true;
            RefreshPendingCore();
        }
    }

    /// <summary>Moving a file that is still being written fails with a sharing violation; say so plainly.</summary>
    private static string DescribeArchiveFailure(Exception exception)
    {
        const int SharingViolation = unchecked((int)0x80070020);
        const int LockViolation = unchecked((int)0x80070021);
        return exception is IOException && (exception.HResult == SharingViolation || exception.HResult == LockViolation)
            ? "文件仍在使用中，请等复制完成后再点击归档。"
            : $"归档失败：{exception.Message}";
    }

    private void OnAlwaysOnTopClick(object sender, RoutedEventArgs e)
    {
        // The target state comes from the app, not from the toggle: one writer, no event ping-pong.
        var pinned = !_context.IsPinned;
        _context.SetPinned(pinned);
        AlwaysOnTopButton.IsChecked = pinned;
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = pinned;
        PendingSubtitle.Text = pinned ? "置顶模式 · 确认归档前不会移动文件" : "点击归档才会移动文件";
        ApplyPinnedOpacity();
        ReapplyPinHitTest();
        RefreshPendingCore();
    }

    private void ApplyPinnedOpacity() => RootSurface.Opacity = _context.IsPinned
        ? Math.Clamp(_context.Settings.PanelOpacity, AppSettings.MinPanelOpacity, 1.0)
        : 1.0;

    private void ApplyBackdrop() =>
        WindowChrome.ApplyBackdrop(this, RootSurface, _context.Settings.Backdrop);

    private async void OnOpenSettings(object sender, RoutedEventArgs e) => await OpenSettingsAsync();

    private async Task OpenSettingsAsync()
    {
        if (_busy)
        {
            return;
        }

        var settings = new SettingsWindow(_context.Settings.Clone(), this, _context.ProbeHotkey);
        var updated = await settings.ShowAsync();
        if (updated is null)
        {
            return;
        }

        var hotkeyError = _context.ApplySettings(updated);
        AlwaysOnTopButton.IsChecked = _context.IsPinned;
        ApplyPinnedOpacity();
        ApplyBackdrop();
        RefreshPendingCore();
        if (hotkeyError is not null)
        {
            ShowStatus(hotkeyError, isError: true);
        }
    }

    private void UpdateStatus()
    {
        if (!_context.HasTarget)
        {
            ShowStatus("请先在设置中选择目标目录。", isError: true);
        }
        else
        {
            StatusText.Text = _context.IsPinned
                ? "置顶待命 · 文件只会在点击归档后移动"
                : "监控中 · 有新文件时任务栏图标会闪烁";
            StatusText.Foreground = (Brush)Application.Current.Resources["AppMutedBrush"];
        }

        InboxSubtitle.Text = _context.Settings.InboxPath;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)Application.Current.Resources[isError ? "AppDangerBrush" : "AppMutedBrush"];
    }
}
