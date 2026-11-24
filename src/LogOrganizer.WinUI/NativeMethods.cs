using System.Runtime.InteropServices;
using System.Text;

namespace LogOrganizer.WinUI;

internal static class NativeMethods
{
    internal const int WH_KEYBOARD_LL = 13;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int VK_RETURN = 0x0D;
    internal const int VK_SPACE = 0x20;
    internal const int GWL_EXSTYLE = -20;
    internal const int WS_EX_NOACTIVATE = 0x08000000;
    internal const int WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly string[] EditLikeClasses = { "Edit", "RichEdit", "Scintilla", "TEdit", "TSpinEdit" };

    internal delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    internal static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    internal static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(IntPtr window, string text, string caption, uint type);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr window);

    /// <summary>Window DPI divided by 96; AppWindow sizes are physical pixels, XAML sizes are DIP.</summary>
    internal static double DpiScale(IntPtr window)
    {
        var dpi = GetDpiForWindow(window);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    internal static void ShowMessageBox(IntPtr window, string text, string caption, uint type) =>
        MessageBox(window, text, caption, type);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GuiThreadInfo
    {
        public int Size;
        public int Flags;
        public IntPtr ActiveWindow;
        public IntPtr FocusWindow;
        public IntPtr CaptureWindow;
        public IntPtr MenuOwnerWindow;
        public IntPtr MoveSizeWindow;
        public IntPtr CaretWindow;
        public Rect CaretRect;
    }

    internal static bool IsTextInputFocused(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        var threadId = GetWindowThreadProcessId(foregroundWindow, out _);
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        if (threadId == 0 || !GetGUIThreadInfo(threadId, ref info))
        {
            return false;
        }

        if (info.CaretWindow != IntPtr.Zero || info.FocusWindow == IntPtr.Zero)
        {
            return info.CaretWindow != IntPtr.Zero;
        }

        var className = new StringBuilder(256);
        return GetClassName(info.FocusWindow, className, className.Capacity) > 0
            && EditLikeClasses.Any(name => className.ToString().StartsWith(name, StringComparison.OrdinalIgnoreCase));
    }
}
