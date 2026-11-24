using LogOrganizer.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace LogOrganizer.WinUI;

public sealed partial class SettingsWindow : Window
{
    private static readonly BackdropMode[] BackdropOrder =
    {
        BackdropMode.Mica,
        BackdropMode.Acrylic,
        BackdropMode.Solid,
    };

    private readonly AppSettings _current;
    private readonly Window? _owner;
    private TaskCompletionSource<AppSettings?>? _completion;

    public SettingsWindow(AppSettings current, Window? owner = null)
    {
        _current = current;
        _owner = owner;
        InitializeComponent();

        WindowChrome.ResizeDip(this, 540, 820, owner);
        AppWindow.Title = "日志归档助手 - 设置";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        if (owner is not null)
        {
            WindowChrome.CenterOver(owner, this);
        }

        InboxRootBox.Text = current.InboxRoot;
        InboxNameBox.Text = current.InboxFolderName;
        TargetBox.Text = current.TargetRoot ?? string.Empty;
        QuietSecondsBox.Value = current.QuietSeconds;
        StartupBox.IsChecked = current.StartWithWindows;
        WakeBox.IsChecked = current.WakeWithSpaceOrEnter;
        PinnedBox.IsChecked = current.PanelPinned;
        OpacitySlider.Value = Math.Round(current.PanelOpacity * 100);
        BackdropBox.SelectedIndex = Math.Max(0, Array.IndexOf(BackdropOrder, current.Backdrop));
        VersionText.Text = $"版本 {AppVersion.Display}";
        UpdatePreview();

        TitleBar.Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyBackdrop();
        UpdateTitleBarLayout();
        if (TitleBar.XamlRoot is not null)
        {
            TitleBar.XamlRoot.Changed += OnXamlRootChanged;
        }

        RootSurface.ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateTitleBarLayout();

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyBackdrop();
        UpdateTitleBarLayout();
    }

    private void ApplyBackdrop() =>
        WindowChrome.ApplyBackdrop(this, RootSurface, SelectedBackdrop());

    private BackdropMode SelectedBackdrop() =>
        BackdropBox.SelectedIndex >= 0 && BackdropBox.SelectedIndex < BackdropOrder.Length
            ? BackdropOrder[BackdropBox.SelectedIndex]
            : BackdropMode.Mica;

    /// <summary>No custom buttons here, so the header only needs to stay clear of the caption area.</summary>
    private void UpdateTitleBarLayout()
    {
        if (!ExtendsContentIntoTitleBar || TitleBar.XamlRoot is null)
        {
            return;
        }

        var inset = WindowChrome.CaptionInsetDip(this, TitleBar.XamlRoot);
        TitleBarHeader.Padding = new Thickness(20, 14, inset + 8, 14);
    }

    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e) => ApplyBackdrop();

    public async Task<AppSettings?> ShowAsync()
    {
        _completion = new TaskCompletionSource<AppSettings?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Activate();
        return await _completion.Task;
    }

    private void OnClosed(object sender, WindowEventArgs args) => _completion?.TrySetResult(null);

    private void OnInboxChanged(object sender, TextChangedEventArgs args) => UpdatePreview();

    private void UpdatePreview()
    {
        var root = InboxRootBox?.Text.Trim() ?? string.Empty;
        var name = InboxNameBox?.Text.Trim() ?? string.Empty;
        if (root.Length == 0 || name.Length == 0)
        {
            InboxPreview.Text = "中转目录：未设置";
            return;
        }

        try
        {
            InboxPreview.Text = "中转目录：" + Path.Combine(Path.GetFullPath(root), name);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            InboxPreview.Text = "中转目录：路径无效";
        }
    }

    private async void OnBrowseInbox(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await PickFolderAsync();
            if (path is not null)
            {
                InboxRootBox.Text = path;
            }
        }
        catch (Exception ex)
        {
            ValidationText.Text = $"无法打开目录选择器：{ex.Message}";
        }
    }

    private async void OnBrowseTarget(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = await PickFolderAsync();
            if (path is not null)
            {
                TargetBox.Text = path;
            }
        }
        catch (Exception ex)
        {
            ValidationText.Text = $"无法打开目录选择器：{ex.Message}";
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private void OnOpacityChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (OpacityValue is not null)
        {
            OpacityValue.Text = $"{e.NewValue:0}%";
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var root = InboxRootBox.Text.Trim();
        var folderName = InboxNameBox.Text.Trim();
        var target = TargetBox.Text.Trim();
        if (root.Length == 0)
        {
            ValidationText.Text = "请填写中转目录根路径。";
            InboxRootBox.Focus(FocusState.Programmatic);
            return;
        }

        if (!PathSafety.IsSimpleName(folderName))
        {
            ValidationText.Text = "中转文件夹名称无效，不能包含路径分隔符或文件名非法字符。";
            InboxNameBox.Focus(FocusState.Programmatic);
            return;
        }

        string inbox;
        try
        {
            inbox = Path.Combine(Path.GetFullPath(root), folderName);
            Directory.CreateDirectory(inbox);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            ValidationText.Text = $"无法创建中转目录：{ex.Message}";
            return;
        }

        if (target.Length > 0 && !Directory.Exists(target))
        {
            ValidationText.Text = "目标目录不存在，请先创建或选择一个已有目录。";
            TargetBox.Focus(FocusState.Programmatic);
            return;
        }

        var updated = SettingsStore.Normalize(new AppSettings
        {
            InboxRoot = root,
            InboxFolderName = folderName,
            TargetRoot = target.Length == 0 ? null : target,
            QuietSeconds = (int)Math.Clamp(QuietSecondsBox.Value, AppSettings.MinQuietSeconds, AppSettings.MaxQuietSeconds),
            StartWithWindows = StartupBox.IsChecked == true,
            WakeWithSpaceOrEnter = WakeBox.IsChecked == true,
            PanelPinned = PinnedBox.IsChecked == true,
            Backdrop = SelectedBackdrop(),
            PanelOpacity = OpacitySlider.Value / 100.0,
            PanelLeft = _current.PanelLeft,
            PanelTop = _current.PanelTop,
        });

        _completion?.TrySetResult(updated);
        _completion = null;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _completion?.TrySetResult(null);
        _completion = null;
        Close();
    }
}