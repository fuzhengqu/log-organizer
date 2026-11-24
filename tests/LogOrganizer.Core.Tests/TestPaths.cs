namespace LogOrganizer.Core.Tests;

/// <summary>
/// Creates disposable test directories. Some restricted environments block writes under the
/// system temp folder, so fall back to the test output directory.
/// </summary>
internal static class TestPaths
{
    private static readonly Lazy<string> Root = new(ResolveRoot);

    public static string NewDirectory()
    {
        var path = Path.Combine(Root.Value, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveRoot()
    {
        var candidates = new List<string>();

        var configured = Environment.GetEnvironmentVariable("TFO_TEST_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.Add(configured);
        }

        candidates.Add(Path.Combine(Path.GetTempPath(), "tfo-tests"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "_test-temp"));

        foreach (var candidate in candidates)
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(probe);
                Directory.Delete(probe);
                return candidate;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        throw new InvalidOperationException("No writable directory is available for tests.");
    }

    public static void DeleteQuietly(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}