using LogOrganizer.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace LogOrganizer.WinUI;

public sealed partial class SettingsWindow : Window
{
    private const double MinWindowWidthDip = 460;
    private const double MinWindowHeightDip = 560;

    private static readonly BackdropMode[] BackdropOrder =
    {
        BackdropMode.Mica,
        BackdropMode.Acrylic,
        BackdropMode.Solid,
    };

    private const string HotkeyHintText =
        "默认为 Ctrl+Shift+L。启用后本程序会独占该组合（例如 VS Code 里同名的“选中所有匹配项”会失效）。";

    private readonly AppSettings _current;
    private readonly Func<HotkeyGesture, string?>? _probeHotkey;
    private TaskCompletionSource<AppSettings?>? _completion;
    private HotkeyGesture _hotkey;
    private bool _capturingHotkey;

    public SettingsWindow(AppSettings current, Window? owner = null, Func<HotkeyGesture, string?>? probeHotkey = null)
    {
        _current = current;
        _probeHotkey = probeHotkey;
        InitializeComponent();

        WindowChrome.ResizeDip(this, 540, 820, owner);
        AppWindow.Title = "日志归档助手 - 设置";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        var presenter = (OverlappedPresenter)AppWindow.Presenter;
        presenter.IsResizable = true;
        presenter.IsMaximizable = true;
        presenter.IsMinimizable = true;
        ApplyMinimumSize(presenter);
        AppIcon.Apply(this);
        if (owner is not null)
        {
            WindowChrome.CenterOver(owner, this);
        }

        _hotkey = HotkeyGesture.TryParse(current.WakeHotkey, out var parsed) ? parsed : HotkeyGesture.Default;

        InboxRootBox.Text = current.InboxRoot;
        InboxNameBox.Text = current.InboxFolderName;
        TargetBox.Text = current.TargetRoot ?? string.Empty;
        StartupBox.IsChecked = current.StartWithWindows;
        HotkeyBox.IsChecked = current.WakeHotkeyEnabled;
        PinnedBox.IsChecked = current.PanelPinned;
        OpacitySlider.Value = Math.Round(current.PanelOpacity * 100);
        BackdropBox.SelectedIndex = Math.Max(0, Array.IndexOf(BackdropOrder, current.Backdrop));
        VersionText.Text = $"版本 {AppVersion.Display}";
        HotkeyHint.Text = HotkeyHintText;
        UpdateHotkeyButton();
        UpdatePreview();

        TitleBar.Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>OverlappedPresenter takes physical pixels, so the floor follows the monitor scale.</summary>
    private void ApplyMinimumSize(OverlappedPresenter presenter)
    {
        var scale = NativeMethods.DpiScale(WindowNative.GetWindowHandle(this));
        presenter.PreferredMinimumWidth = (int)Math.Round(MinWindowWidthDip * scale);
        presenter.PreferredMinimumHeight = (int)Math.Round(MinWindowHeightDip * scale);
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

    private void OnHotkeyToggled(object sender, RoutedEventArgs e)
    {
        // Only the hint changes; the combination is still editable while disabled.
        UpdateHotkeyButton();
    }

    private void UpdateHotkeyButton()
    {
        if (HotkeyButton is null || _capturingHotkey)
        {
            return;
        }

        HotkeyButton.Content = _hotkey.ToString();
        AutomationProperties.SetName(HotkeyButton, $"唤醒快捷键：{_hotkey}");
    }

    private void OnHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        if (_capturingHotkey)
        {
            EndHotkeyCapture();
            return;
        }

        _capturingHotkey = true;
        HotkeyButton.Content = "请按下组合键…（Esc 取消）";
        AutomationProperties.SetName(HotkeyButton, "请按下组合键");
        HotkeyHint.Text = "按下任意“修饰键 + 单键”组合；Esc 取消。";
        HotkeyButton.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// PreviewKeyDown runs before the button turns Space/Enter into a Click, so a shortcut that uses
    /// those keys is recorded instead of re-triggering the button.
    /// </summary>
    private void OnHotkeyCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturingHotkey)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            EndHotkeyCapture();
            return;
        }

        var virtualKey = (uint)e.Key;
        if (HotkeyGesture.IsModifierKey(virtualKey))
        {
            return;
        }

        if (!HotkeyGesture.TryCreate(CurrentModifiers(), virtualKey, out var gesture))
        {
            HotkeyHint.Text = "需要至少一个 Ctrl / Alt / Shift / Win 修饰键；F12 保留给调试器，部分按键无法命名。";
            return;
        }

        _hotkey = gesture;
        EndHotkeyCapture();
    }

    private void EndHotkeyCapture()
    {
        _capturingHotkey = false;
        HotkeyHint.Text = HotkeyHintText;
        UpdateHotkeyButton();
    }

    private static HotkeyModifiers CurrentModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (IsDown(VirtualKey.Menu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (IsDown(VirtualKey.Shift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            modifiers |= HotkeyModifiers.Windows;
        }

        return modifiers;
    }

    private static bool IsDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnHotkeyReset(object sender, RoutedEventArgs e)
    {
        _hotkey = HotkeyGesture.Default;
        EndHotkeyCapture();
    }

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

        var hotkeyEnabled = HotkeyBox.IsChecked == true;
        if (hotkeyEnabled && _probeHotkey?.Invoke(_hotkey) is { } hotkeyError)
        {
            ValidationText.Text = hotkeyError;
            HotkeyButton.Focus(FocusState.Programmatic);
            return;
        }

        var updated = SettingsStore.Normalize(new AppSettings
        {
            InboxRoot = root,
            InboxFolderName = folderName,
            TargetRoot = target.Length == 0 ? null : target,
            StartWithWindows = StartupBox.IsChecked == true,
            WakeHotkeyEnabled = hotkeyEnabled,
            WakeHotkey = _hotkey.ToString(),
            PanelPinned = PinnedBox.IsChecked == true,
            Backdrop = SelectedBackdrop(),
            PanelOpacity = OpacitySlider.Value / 100.0,
            PanelLeft = _current.PanelLeft,
            PanelTop = _current.PanelTop,
            PanelWidth = _current.PanelWidth,
            PanelHeight = _current.PanelHeight,
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