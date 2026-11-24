using System.Runtime.InteropServices;

namespace LogOrganizer.WinUI;

internal sealed class WakeKeyHook : IDisposable
{
    private readonly Func<bool> _shouldWake;
    private readonly Action _wake;
    private readonly NativeMethods.HookProc _callback;
    private IntPtr _handle;

    public WakeKeyHook(Func<bool> shouldWake, Action wake)
    {
        _shouldWake = shouldWake;
        _wake = wake;
        _callback = OnCallback;
        _handle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _callback,
            NativeMethods.GetModuleHandle(null),
            0);
    }

    private IntPtr OnCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
        {
            var key = Marshal.ReadInt32(data);
            if (key is NativeMethods.VK_RETURN or NativeMethods.VK_SPACE && SafeShouldWake())
            {
                _wake();
                return 1;
            }
        }

        return NativeMethods.CallNextHookEx(_handle, code, message, data);
    }

    private bool SafeShouldWake()
    {
        try
        {
            return _shouldWake();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_handle);
        _handle = IntPtr.Zero;
    }
}
