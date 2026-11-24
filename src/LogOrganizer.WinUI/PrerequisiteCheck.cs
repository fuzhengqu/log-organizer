using System.Runtime.InteropServices;

namespace LogOrganizer.WinUI;

/// <summary>
/// Checks the runtime pieces the app cannot ship by itself, before WinUI tries to load them.
/// Without this the process would die with 0xC0000135 and no explanation.
/// </summary>
internal static class PrerequisiteCheck
{
    /// <summary>Windows 10 1809 (build 17763) is the minimum the Windows App SDK 1.8 supports.</summary>
    private const int MinimumWindowsBuild = 17763;

    private static readonly string[] NativeRuntimeFiles =
    {
        "vcruntime140.dll",
        "vcruntime140_1.dll",
        "msvcp140.dll",
    };

    internal static bool TryVerify(out string? message)
    {
        var version = Environment.OSVersion.Version;
        if (version.Major < 10 || (version.Major == 10 && version.Build < MinimumWindowsBuild))
        {
            message = $"日志归档助手需要 Windows 10 1809（内部版本 {MinimumWindowsBuild}）或更高版本。\r\n"
                + $"当前系统版本：{version}。";
            return false;
        }

        var missing = new List<string>();
        foreach (var file in NativeRuntimeFiles)
        {
            if (!NativeLibrary.TryLoad(file, out var handle))
            {
                missing.Add(file);
                continue;
            }

            NativeLibrary.Free(handle);
        }

        if (missing.Count == 0)
        {
            message = null;
            return true;
        }

        var installer = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "VC_redist.arm64.exe"
            : "VC_redist.x64.exe";
        var local = Path.Combine(AppContext.BaseDirectory, installer);
        var hint = File.Exists(local)
            ? $"请先运行同目录下的 {installer}，完成后重新打开本程序。"
            : $"请先安装 Microsoft Visual C++ 2015-2022 运行库（{installer}），\r\n"
                + $"下载地址：https://aka.ms/vs/17/release/{installer}\r\n安装完成后重新打开本程序。";

        message = $"缺少系统运行库：{string.Join("、", missing)}\r\n\r\n{hint}\r\n\r\n"
            + "说明：.NET 与 Windows App Runtime 已包含在程序内，无需另行安装。";
        return false;
    }
}