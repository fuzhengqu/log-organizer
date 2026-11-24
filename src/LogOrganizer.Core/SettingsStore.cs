using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LogOrganizer.Core;

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON, recovering to defaults when unusable.</summary>
public sealed class SettingsStore
{
    /// <summary>Folder name used before the rename to Log Organizer.</summary>
    public const string LegacyDirectoryName = "ticket-file-organizer";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly bool _isDefaultDirectory;

    public SettingsStore(string? baseDirectory = null)
    {
        _isDefaultDirectory = string.IsNullOrWhiteSpace(baseDirectory);
        BaseDirectory = _isDefaultDirectory
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "log-organizer")
            : baseDirectory!;
    }

    public string BaseDirectory { get; }

    public string FilePath => Path.Combine(BaseDirectory, "config.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                TryMigrateLegacyConfig();
            }

            if (!File.Exists(FilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(FilePath);
            var node = JsonNode.Parse(json);
            if (node is JsonObject root)
            {
                MigrateLegacyBackdrop(root);
            }

            var loaded = node?.Deserialize<AppSettings>(Options);
            return loaded is null ? new AppSettings() : Normalize(loaded);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Carries settings over from the pre-rename folder so an upgrade does not look like a fresh install.
    /// The old file is copied, never deleted or modified.
    /// </summary>
    private void TryMigrateLegacyConfig()
    {
        if (!_isDefaultDirectory)
        {
            return;
        }

        try
        {
            var roaming = Path.GetDirectoryName(BaseDirectory);
            if (string.IsNullOrEmpty(roaming))
            {
                return;
            }

            var legacy = Path.Combine(roaming, LegacyDirectoryName, "config.json");
            if (!File.Exists(legacy))
            {
                return;
            }

            Directory.CreateDirectory(BaseDirectory);
            File.Copy(legacy, FilePath, overwrite: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Migration is best effort: defaults are still usable.
        }
    }

    /// <summary>Older configs stored a single <c>UseMicaBackdrop</c> flag instead of <see cref="AppSettings.Backdrop"/>.</summary>
    private static void MigrateLegacyBackdrop(JsonObject root)
    {
        if (root.ContainsKey(nameof(AppSettings.Backdrop)))
        {
            return;
        }

        if (!root.TryGetPropertyValue("UseMicaBackdrop", out var legacy) || legacy is null)
        {
            return;
        }

        var useMica = legacy.GetValueKind() != JsonValueKind.False;
        root[nameof(AppSettings.Backdrop)] = useMica
            ? nameof(BackdropMode.Mica)
            : nameof(BackdropMode.Solid);
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(BaseDirectory);

        var normalized = Normalize(settings);
        var json = JsonSerializer.Serialize(normalized, Options);

        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, FilePath, true);
    }

    /// <summary>Repairs values that cannot work, so a hand-edited file never breaks startup.</summary>
    public static AppSettings Normalize(AppSettings settings)
    {
        var normalized = settings.Clone();

        if (string.IsNullOrWhiteSpace(normalized.InboxRoot) || !IsValidPath(normalized.InboxRoot))
        {
            normalized.InboxRoot = AppSettings.DefaultInboxRoot;
        }

        if (!PathSafety.IsSimpleName(normalized.InboxFolderName))
        {
            normalized.InboxFolderName = AppSettings.DefaultInboxFolderName;
        }

        normalized.InboxRoot = normalized.InboxRoot!.Trim();
        normalized.InboxFolderName = normalized.InboxFolderName!.Trim();

        if (normalized.QuietSeconds < AppSettings.MinQuietSeconds)
        {
            normalized.QuietSeconds = AppSettings.MinQuietSeconds;
        }
        else if (normalized.QuietSeconds > AppSettings.MaxQuietSeconds)
        {
            normalized.QuietSeconds = AppSettings.MaxQuietSeconds;
        }

        if (string.IsNullOrWhiteSpace(normalized.TargetRoot) || !IsValidPath(normalized.TargetRoot))
        {
            normalized.TargetRoot = null;
        }
        else
        {
            normalized.TargetRoot = normalized.TargetRoot!.Trim();
        }

        if (double.IsNaN(normalized.PanelOpacity) || double.IsInfinity(normalized.PanelOpacity))
        {
            normalized.PanelOpacity = AppSettings.DefaultPanelOpacity;
        }
        else
        {
            normalized.PanelOpacity = Math.Clamp(normalized.PanelOpacity, AppSettings.MinPanelOpacity, 1.0);
        }

        if (normalized.PanelLeft < -1)
        {
            normalized.PanelLeft = -1;
        }

        if (normalized.PanelTop < -1)
        {
            normalized.PanelTop = -1;
        }

        normalized.PanelWidth = ClampSize(normalized.PanelWidth, AppSettings.MinStoredPanelWidth);
        normalized.PanelHeight = ClampSize(normalized.PanelHeight, AppSettings.MinStoredPanelHeight);

        if (!HotkeyGesture.TryParse(normalized.WakeHotkey, out var gesture))
        {
            gesture = HotkeyGesture.Default;
        }

        normalized.WakeHotkey = gesture.ToString();

        if (!Enum.IsDefined(normalized.Backdrop))
        {
            normalized.Backdrop = BackdropMode.Mica;
        }

        return normalized;
    }

    /// <summary>0 (unset) stays unset; anything else is a real size, so it gets clamped instead of dropped.</summary>
    private static int ClampSize(int value, int minimum) => value switch
    {
        <= 0 => 0,
        _ => Math.Clamp(value, minimum, AppSettings.MaxStoredPanelSize),
    };

    private static bool IsValidPath(string path)
    {
        try
        {
            Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}