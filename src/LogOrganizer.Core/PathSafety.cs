namespace LogOrganizer.Core;

/// <summary>Guards against paths that escape the configured roots through reparse points.</summary>
public static class PathSafety
{
    public static bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Reports whether <paramref name="path"/> or one of its ancestors below its root is a reparse point.</summary>
    public static bool HasReparsePointInChain(string path)
    {
        var current = Path.GetFullPath(path);
        var root = Path.GetPathRoot(current);

        while (!string.IsNullOrEmpty(current) && !string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
        {
            if (IsReparsePoint(current))
            {
                return true;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>Rejects names that are empty, contain separators, or are otherwise unusable as a file name.</summary>
    public static bool IsSimpleName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (name is "." or "..")
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return !name.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !name.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}