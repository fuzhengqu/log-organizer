namespace LogOrganizer.WinUI;

/// <summary>Writes startup failures to a local file so a crash can be diagnosed without a debugger.</summary>
internal static class CrashLog
{
    private const long MaxBytes = 1L * 1024 * 1024;

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LogOrganizer",
        "crash.log");

    public static void Write(Exception? exception, string context)
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);

            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length > MaxBytes)
            {
                file.Delete();
            }

            File.AppendAllText(FilePath,
                $"[{DateTimeOffset.Now:O}] {context}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}