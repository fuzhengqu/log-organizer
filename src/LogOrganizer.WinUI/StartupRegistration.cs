using System.Security;
using Microsoft.Win32;

namespace LogOrganizer.WinUI;

internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LogOrganizer";
    private const string LegacyValueName = "TicketFileOrganizer";

    public static string ExecutablePath => Environment.ProcessPath ?? AppContext.BaseDirectory;

    public static bool TryApply(bool enabled, out string? error)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null)
            {
                error = "无法打开当前用户的开机启动注册表项。";
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\"");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            // The pre-rename build wrote a different value name; leave no orphan behind.
            if (key.GetValue(LegacyValueName) is not null)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            error = ex.Message;
            return false;
        }
    }
}
