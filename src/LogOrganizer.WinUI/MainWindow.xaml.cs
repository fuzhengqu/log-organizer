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
    private readonly TrayApplicationContext _context;
    private bool _refreshing;
    private bool _busy;
    private bool _isClosing;
    private bool _showingQuietPrompt;
    private bool _shutdown;
    private WakeKeyHook? _wakeHook;

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
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = true;
        AppWindow.Changed += OnWindowChanged;
        AppWindow.Closing += OnWindowClosing;
        TitleBar.Loaded += OnLoaded;
        Closed += OnClosed;
        _context.PendingChanged += OnPendingChanged;
        _context.QuietActivity += OnQuietActivity;
        AlwaysOnTopButton.IsChecked = _context.IsPinned;
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = _context.IsPinned;
        RefreshPending();
        UpdateStatus();
        UpdateTrayMenu();
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange)
        {
            UpdateTitleBarLayout();
        }

        if (args.DidPositionChange && Visible && !_context.IsPinned)
        {
            _context.SaveWindowPosition(AppWindow.Position.X, AppWindow.Position.Y);
        }
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

    public void ShowArchiveReady()
    {
        if (!_context.IsPinned)
        {
            ShowQuietPrompt();
        }
    }

    private void ShowQuietPrompt()
    {
        if (_context.IsPinned || _showingQuietPrompt || _shutdown)
        {
            return;
        }

        _showingQuietPrompt = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = Content.XamlRoot,
                    Title = "项目已准备归档",
                    Content = $"静默等待结束，当前有 {_context.GetPendingItems().Count} 个项目。输入问题单号后点击归档。",
                    PrimaryButtonText = "填写单号",
                    CloseButtonText = "稍后",
                };
                await dialog.ShowAsync();
                if (!_context.IsPinned && !_shutdown)
                {
                    _context.ShowWindow();
                    TicketBox.Focus(FocusState.Programmatic);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                CrashLog.Write(ex, "显示静默期提示失败");
            }
            finally
            {
                _showingQuietPrompt = false;
            }
        });
    }

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
        _wakeHook?.Dispose();
        _wakeHook = null;
        Closed -= OnClosed;
        AppWindow.Closing -= OnWindowClosing;
        AppWindow.Changed -= OnWindowChanged;
        _context.PendingChanged -= OnPendingChanged;
        _context.QuietActivity -= OnQuietActivity;
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
        _isClosing = true;
        _wakeHook?.Dispose();
        _context.PendingChanged -= OnPendingChanged;
        _context.QuietActivity -= OnQuietActivity;
        if (!_shutdown)
        {
            _context.Shutdown();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var saved = _context.Settings;
        if (saved.PanelLeft >= 0 && saved.PanelTop >= 0)
        {
            AppWindow.Move(new Windows.Graphics.PointInt32(saved.PanelLeft, saved.PanelTop));
        }
        else
        {
            CenterOnScreen();
        }

        if (_context.Settings.WakeWithSpaceOrEnter)
        {
            _wakeHook = new WakeKeyHook(ShouldWake, FocusTicketBox);
        }

        ApplyPinnedOpacity();
        ApplyBackdrop();
        UpdateTitleBarLayout();
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
    }

    private void CenterOnScreen()
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        if (display is null)
        {
            return;
        }

        var area = display.WorkArea;
        AppWindow.Move(new Windows.Graphics.PointInt32(
            area.X + (area.Width - AppWindow.Size.Width) / 2,
            area.Y + (area.Height - AppWindow.Size.Height) / 2));
    }

    private bool ShouldWake()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return !_isClosing && foreground != WindowNative.GetWindowHandle(this)
            && !NativeMethods.IsTextInputFocused(foreground);
    }

    private void OnPendingChanged(object? sender, EventArgs e) => RefreshPending();

    private void OnQuietActivity(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_context.IsPinned)
            {
                StatusText.Text = $"检测到文件变化，静默等待 {_context.Settings.QuietSeconds} 秒…";
                StatusText.Foreground = (Brush)Application.Current.Resources["AppMutedBrush"];
            }
        });
    }

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
                : "静默期结束后提示，点击归档才会移动文件";
            ArchiveButton.IsEnabled = items.Count > 0 && TicketNumber.TryParse(TicketBox.Text.Trim(), out _)
                && _context.HasTarget && !_context.IsPaused;
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
            ShowStatus($"归档失败：{ex.Message}", isError: true);
        }
        finally
        {
            _busy = false;
            SettingsButton.IsEnabled = true;
            RefreshPendingCore();
        }
    }

    private void OnAlwaysOnTopChanged(object sender, RoutedEventArgs e)
    {
        if (_context.IsPaused)
        {
            AlwaysOnTopButton.IsChecked = _context.IsPinned;
            ShowStatus("监控已暂停，请先恢复监控再切换置顶模式。", isError: true);
            return;
        }

        var pinned = AlwaysOnTopButton.IsChecked == true;
        _context.SetPinned(pinned);
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = pinned;
        ToolTipService.SetToolTip(AlwaysOnTopButton, pinned ? "取消置顶，恢复静默期归档提示" : "置顶窗口并停用静默期弹窗");
        PendingSubtitle.Text = pinned ? "置顶模式 · 确认归档前不会移动文件" : "静默期结束后提示，点击归档才会移动文件";
        ApplyPinnedOpacity();
        RefreshPendingCore();
    }

    private void ApplyPinnedOpacity() => RootSurface.Opacity = _context.IsPinned
        ? Math.Clamp(_context.Settings.PanelOpacity, AppSettings.MinPanelOpacity, 1.0)
        : 1.0;

    private void ApplyBackdrop() =>
        WindowChrome.ApplyBackdrop(this, RootSurface, _context.Settings.Backdrop);

    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var settings = new SettingsWindow(_context.Settings.Clone(), this);
        var updated = await settings.ShowAsync();
        if (updated is null)
        {
            return;
        }

        _context.ApplySettings(updated);
        AlwaysOnTopButton.IsChecked = _context.IsPinned;
        ((OverlappedPresenter)AppWindow.Presenter).IsAlwaysOnTop = _context.IsPinned;
        ApplyPinnedOpacity();
        ApplyBackdrop();
        RefreshPendingCore();
        UpdateTrayMenu();
        if (_context.Settings.WakeWithSpaceOrEnter && _wakeHook is null)
        {
            _wakeHook = new WakeKeyHook(ShouldWake, FocusTicketBox);
        }
        else if (!_context.Settings.WakeWithSpaceOrEnter)
        {
            _wakeHook?.Dispose();
            _wakeHook = null;
        }
    }

    private void OnOpenInbox(object sender, RoutedEventArgs e) => _context.OpenFolder(_context.Settings.InboxPath);
    private void OnOpenTarget(object sender, RoutedEventArgs e) => _context.OpenFolder(_context.Settings.TargetRoot);

    private void OnTogglePause(object sender, RoutedEventArgs e)
    {
        _context.TogglePause();
        RefreshPendingCore();
    }

    public void RefreshTrayMenu() => UpdateTrayMenu();

    private void UpdateTrayMenu()
    {
        if (PauseMenuItem is null)
        {
            return;
        }

        PauseMenuItem.Text = _context.IsPaused ? "继续监控" : "暂停监控";
        OpenTargetMenuItem.IsEnabled = _context.HasTarget;
        ToolTipService.SetToolTip(AlwaysOnTopButton, _context.IsPinned ? "取消置顶，恢复静默期归档提示" : "置顶窗口并停用静默期弹窗");
    }

    private void UpdateStatus()
    {
        if (_context.IsPaused)
        {
            ShowStatus("监控已暂停 · 项目不会自动归档", isError: false);
        }
        else if (!_context.HasTarget)
        {
            ShowStatus("请先在设置中选择目标目录。", isError: true);
        }
        else
        {
            StatusText.Text = _context.IsPinned
                ? "置顶待命 · 文件只会在点击归档后移动"
                : $"监控中 · 静默期 {_context.Settings.QuietSeconds} 秒";
            StatusText.Foreground = (Brush)Application.Current.Resources["AppMutedBrush"];
        }

        InboxSubtitle.Text = _context.Settings.InboxPath;
    }

    private void ShowStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)Application.Current.Resources[isError ? "AppDangerBrush" : "AppMutedBrush"];
    }

    private void OnExit(object sender, RoutedEventArgs e) => _context.Shutdown();

    private void OnToggleWindow(object sender, RoutedEventArgs e) => _context.ToggleWindow();
}
