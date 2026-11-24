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

    public string InboxRoot { get; set; } = DefaultInboxRoot;

    public string InboxFolderName { get; set; } = DefaultInboxFolderName;

    /// <summary>Unset until the user configures one.</summary>
    public string? TargetRoot { get; set; }

    public int QuietSeconds { get; set; } = DefaultQuietSeconds;

    /// <summary>Opt-in; disabled by default.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Lets Space or Enter wake the archive prompt without stealing focus up front.</summary>
    public bool WakeWithSpaceOrEnter { get; set; } = true;

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
        WakeWithSpaceOrEnter = WakeWithSpaceOrEnter,
        PanelPinned = PanelPinned,
        Backdrop = Backdrop,
        PanelOpacity = PanelOpacity,
        PanelLeft = PanelLeft,
        PanelTop = PanelTop,
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