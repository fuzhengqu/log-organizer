using System.Runtime.InteropServices;

namespace LogOrganizer.WinUI;

/// <summary>
/// The tray context menu, drawn by Win32 so it can open at the cursor next to the icon.
/// <para>
/// H.NotifyIcon 2.4.1's default <c>PopupMenu</c> mode is implemented in the separate
/// H.NotifyIcon.PopupMenus package; without it a right click shows nothing at all. Building the
/// menu here keeps every item wired to real code and lets the enabled state follow the app
/// (for example 打开目标目录 stays greyed out until a target folder is configured).
/// </para>
/// </summary>
internal sealed class TrayMenu : IDisposable
{
    private const int IdToggleWindow = 1;
    private const int IdSettings = 2;
    private const int IdOpenInbox = 3;
    private const int IdOpenTarget = 4;
    private const int IdTogglePause = 5;
    private const int IdExit = 6;

    private const uint MfString = 0x00000000;
    private const uint MfGrayed = 0x00000001;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmNoNotify = 0x0080;
    private const uint TpmReturnCmd = 0x0100;
    private const uint WsPopup = 0x80000000;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int WmNull = 0;

    private const string HostClassName = "LogOrganizer.TrayMenuHost";

    /// <summary>Class registration is per process; seeing 1410 again on reload is expected.</summary>
    private static bool _classRegistered;

    private readonly Action _toggleWindow;
    private readonly Action _openSettings;
    private readonly Action _openInbox;
    private readonly Action _openTarget;
    private readonly Action _togglePause;
    private readonly Action _exit;

    /// <summary>
    /// The owner window keeps the menu anchored to this process. It stays hidden, but it is a
    /// normal top-level window (not message-only) so the shell can still make it foreground and
    /// dismiss the menu when the user clicks elsewhere.
    /// </summary>
    private IntPtr _host;

    private TrayMenu(
        Action toggleWindow,
        Action openSettings,
        Action openInbox,
        Action openTarget,
        Action togglePause,
        Action exit,
        IntPtr host)
    {
        _toggleWindow = toggleWindow;
        _openSettings = openSettings;
        _openInbox = openInbox;
        _openTarget = openTarget;
        _togglePause = togglePause;
        _exit = exit;
        _host = host;
    }

    /// <summary>Creates the hidden owner window, or returns null with a user-facing reason.</summary>
    public static TrayMenu? TryCreate(
        Action toggleWindow,
        Action openSettings,
        Action openInbox,
        Action openTarget,
        Action togglePause,
        Action exit,
        out string? error)
    {
        error = null;
        if (!TryEnsureWindowClass())
        {
            error = $"无法注册托盘菜单窗口类（错误 {Marshal.GetLastWin32Error()}）。";
            return null;
        }

        var host = NativeMethods.CreateWindowEx(
            0, HostClassName, string.Empty, WsPopup, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, NativeMethods.GetModuleHandle(null), IntPtr.Zero);
        if (host == IntPtr.Zero)
        {
            error = $"无法创建托盘菜单窗口（错误 {Marshal.GetLastWin32Error()}）。";
            return null;
        }

        return new TrayMenu(toggleWindow, openSettings, openInbox, openTarget, togglePause, exit, host);
    }

    /// <summary>
    /// Opens the menu at a screen position and runs the chosen item. Blocks until the menu closes,
    /// which is how Win32 menus work; the caller must already be on the UI thread.
    /// </summary>
    public void Show(bool windowVisible, bool paused, bool hasTarget, int x, int y)
    {
        if (_host == IntPtr.Zero)
        {
            return;
        }

        var menu = NativeMethods.CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AppendItem(menu, IdToggleWindow, windowVisible ? "隐藏归档窗口" : "显示归档窗口");
            AppendItem(menu, IdSettings, "设置…");
            AppendItem(menu, IdOpenInbox, "打开中转目录");
            AppendItem(menu, IdOpenTarget, "打开目标目录", hasTarget);
            NativeMethods.AppendMenu(menu, MfSeparator, 0, null);
            AppendItem(menu, IdTogglePause, paused ? "继续监控" : "暂停监控");
            AppendItem(menu, IdExit, "退出");

            // Moving the owner onto the tray's monitor makes the menu scale with that display's DPI.
            NativeMethods.SetWindowPos(_host, IntPtr.Zero, x, y, 0, 0, SwpNoZOrder | SwpNoActivate);
            NativeMethods.SetForegroundWindow(_host);
            var command = NativeMethods.TrackPopupMenuEx(
                menu, TpmRightButton | TpmNoNotify | TpmReturnCmd, x, y, _host, IntPtr.Zero);

            // Documented pairing: the posted no-op lets the menu finish closing before the app reacts.
            NativeMethods.PostMessage(_host, WmNull, IntPtr.Zero, IntPtr.Zero);
            Invoke(command);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private static void AppendItem(IntPtr menu, int id, string text, bool enabled = true) =>
        NativeMethods.AppendMenu(menu, MfString | (enabled ? 0 : MfGrayed), (nuint)id, text);

    private void Invoke(int command)
    {
        switch (command)
        {
            case IdToggleWindow:
                _toggleWindow();
                break;
            case IdSettings:
                _openSettings();
                break;
            case IdOpenInbox:
                _openInbox();
                break;
            case IdOpenTarget:
                _openTarget();
                break;
            case IdTogglePause:
                _togglePause();
                break;
            case IdExit:
                _exit();
                break;
        }
    }

    private static bool TryEnsureWindowClass()
    {
        if (_classRegistered)
        {
            return true;
        }

        var wndClass = new NativeMethods.WndClass
        {
            Style = 0,
            WindowProc = Marshal.GetFunctionPointerForDelegate(NativeMethods.WindowProc),
            Instance = NativeMethods.GetModuleHandle(null),
            ClassName = HostClassName,
        };
        if (NativeMethods.RegisterClass(ref wndClass) == 0)
        {
            var code = Marshal.GetLastWin32Error();
            // 1410 = class already registered, which is fine after a previous load in the same process.
            if (code != 1410)
            {
                return false;
            }
        }

        _classRegistered = true;
        return true;
    }

    public void Dispose()
    {
        if (_host == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.DestroyWindow(_host);
        _host = IntPtr.Zero;
    }
}