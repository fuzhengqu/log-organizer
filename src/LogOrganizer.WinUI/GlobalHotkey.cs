using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using LogOrganizer.Core;

namespace LogOrganizer.WinUI;

/// <summary>
/// A system-wide shortcut registered with <c>RegisterHotKey</c>.
/// <para>
/// Unlike the retired low-level keyboard hook this replaced, the OS only reports the exact
/// combination, so ordinary Space/Enter (and every other keystroke) keeps reaching the apps the
/// user is actually typing in. The flip side is that the combination is exclusive: another app
/// holding it makes registration fail, which the caller must surface.
/// </para>
/// </summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0x4C4F;
    private const int ErrorHotkeyAlreadyRegistered = 1409;
    private static readonly IntPtr HwndMessage = new(-3);

    /// <summary>
    /// One window class for the process, plus the handler for each message-only window, so the
    /// window procedure can stay a static method (a delegate tied to a disposed instance would
    /// crash the shared message loop).
    /// </summary>
    private static readonly ConcurrentDictionary<IntPtr, Action> Handlers = new();
    private static readonly NativeMethods.WndProcDelegate WindowProc = OnWindowMessage;
    private static bool _classRegistered;

    private readonly Action _onPressed;
    private IntPtr _window;
    private bool _registered;

    private GlobalHotkey(HotkeyGesture gesture, Action onPressed, IntPtr window)
    {
        Gesture = gesture;
        _onPressed = onPressed;
        _window = window;
        Handlers[window] = RaisePressed;
        _registered = NativeMethods.RegisterHotKey(window, HotkeyId, (uint)gesture.Modifiers, gesture.VirtualKey);
    }

    public HotkeyGesture Gesture { get; }

    /// <summary>Registers the shortcut, or returns null with a user-facing reason.</summary>
    public static GlobalHotkey? TryCreate(HotkeyGesture gesture, Action onPressed, out string? error)
    {
        error = null;
        if (!TryEnsureWindowClass(out error))
        {
            return null;
        }

        var window = NativeMethods.CreateWindowEx(
            0, "LogOrganizer.GlobalHotkey", string.Empty, 0, 0, 0, 0, 0,
            HwndMessage, IntPtr.Zero, NativeMethods.GetModuleHandle(null), IntPtr.Zero);
        if (window == IntPtr.Zero)
        {
            error = $"无法创建快捷键窗口（错误 {Marshal.GetLastWin32Error()}）。";
            return null;
        }

        var hotkey = new GlobalHotkey(gesture, onPressed, window);
        if (!hotkey._registered)
        {
            var code = Marshal.GetLastWin32Error();
            error = code == ErrorHotkeyAlreadyRegistered
                ? $"{gesture} 已被其它程序占用，请换一个组合。"
                : $"无法注册 {gesture}（错误 {code}）。";
            hotkey.Dispose();
            return null;
        }

        return hotkey;
    }

    private void RaisePressed()
    {
        try
        {
            _onPressed();
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, "处理全局快捷键失败");
        }
    }

    private static bool TryEnsureWindowClass(out string? error)
    {
        error = null;
        if (_classRegistered)
        {
            return true;
        }

        var instance = NativeMethods.GetModuleHandle(null);
        var wndClass = new NativeMethods.WndClass
        {
            Style = 0,
            WindowProc = Marshal.GetFunctionPointerForDelegate(WindowProc),
            Instance = instance,
            ClassName = "LogOrganizer.GlobalHotkey",
        };
        if (NativeMethods.RegisterClass(ref wndClass) == 0)
        {
            var code = Marshal.GetLastWin32Error();
            // 1410 = class already registered, which is fine after a previous load in the same process.
            if (code != 1410)
            {
                error = $"无法注册快捷键窗口类（错误 {code}）。";
                return false;
            }
        }

        _classRegistered = true;
        return true;
    }

    private static IntPtr OnWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId && Handlers.TryGetValue(window, out var handler))
        {
            handler();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_window == IntPtr.Zero)
        {
            return;
        }

        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_window, HotkeyId);
            _registered = false;
        }

        Handlers.TryRemove(_window, out _);
        NativeMethods.DestroyWindow(_window);
        _window = IntPtr.Zero;
    }
}