using System.Text.Json.Serialization;

namespace LogOrganizer.Core;

/// <summary>User settings persisted between runs.</summary>
public sealed class AppSettings
{
    public const string DefaultInboxRoot = @"D:\Downloads";
    public const string DefaultInboxFolderName = "_收件";
    public const string DefaultInboxPath = DefaultInboxRoot + @"\" + DefaultInboxFolderName;
    public const int DefaultQuietSeconds = 10;
    public const int MinQuietSeconds = 1;
    public const int MaxQuietSeconds = 600;
    public const double DefaultPanelOpacity = 0.92;
    public const double MinPanelOpacity = 0.40;

    /// <summary>Bounds for a stored window size; 0 means "use the design size".</summary>
    public const int MinStoredPanelWidth = 320;
    public const int MinStoredPanelHeight = 260;
    public const int MaxStoredPanelSize = 20000;

    public string InboxRoot { get; set; } = DefaultInboxRoot;

    public string InboxFolderName { get; set; } = DefaultInboxFolderName;

    /// <summary>Unset until the user configures one.</summary>
    public string? TargetRoot { get; set; }

    public int QuietSeconds { get; set; } = DefaultQuietSeconds;

    /// <summary>Opt-in; disabled by default.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Opt-in; disabled by default. The shortcut is registered only while this is true, because
    /// RegisterHotKey is exclusive and would take the combination away from every other app.
    /// </summary>
    public bool WakeHotkeyEnabled { get; set; }

    /// <summary>Shortcut text in canonical form, e.g. "Ctrl+Shift+L".</summary>
    public string WakeHotkey { get; set; } = HotkeyGesture.DefaultText;

    /// <summary>Shows the manual drop panel in topmost mode; disabled by default.</summary>
    public bool PanelPinned { get; set; }

    /// <summary>Window background material: solid fill, Mica or Acrylic.</summary>
    public BackdropMode Backdrop { get; set; } = BackdropMode.Mica;

    /// <summary>Panel opacity as a value from 0.40 to 1.00.</summary>
    public double PanelOpacity { get; set; } = DefaultPanelOpacity;

    /// <summary>Last panel screen position, or -1 to use the default position.</summary>
    public int PanelLeft { get; set; } = -1;

    /// <summary>Last panel screen position, or -1 to use the default position.</summary>
    public int PanelTop { get; set; } = -1;

    /// <summary>Last panel width in physical pixels, or 0 to use the design width.</summary>
    public int PanelWidth { get; set; }

    /// <summary>Last panel height in physical pixels, or 0 to use the design height.</summary>
    public int PanelHeight { get; set; }

    [JsonIgnore]
    public string InboxPath => Combine(Path.GetFullPath(InboxRoot), InboxFolderName);

    [JsonIgnore]
    public bool HasTarget => !string.IsNullOrWhiteSpace(TargetRoot);

    public TimeSpan QuietPeriod => TimeSpan.FromSeconds(QuietSeconds);

    public AppSettings Clone() => new()
    {
        InboxRoot = InboxRoot,
        InboxFolderName = InboxFolderName,
        TargetRoot = TargetRoot,
        QuietSeconds = QuietSeconds,
        StartWithWindows = StartWithWindows,
        WakeHotkeyEnabled = WakeHotkeyEnabled,
        WakeHotkey = WakeHotkey,
        PanelPinned = PanelPinned,
        Backdrop = Backdrop,
        PanelOpacity = PanelOpacity,
        PanelLeft = PanelLeft,
        PanelTop = PanelTop,
        PanelWidth = PanelWidth,
        PanelHeight = PanelHeight,
    };

    private static string Combine(string root, string name)
    {
        try
        {
            return Path.Combine(root, name);
        }
        catch (ArgumentException)
        {
            return Path.Combine(DefaultInboxRoot, DefaultInboxFolderName);
        }
    }
}