namespace LogOrganizer.Core;

/// <summary>How the main window paints its background.</summary>
public enum BackdropMode
{
    /// <summary>Opaque brand surface; works on every supported Windows version.</summary>
    Solid,

    /// <summary>Windows 11 Mica: desktop-tinted translucent material.</summary>
    Mica,

    /// <summary>Windows acrylic: stronger blur of whatever sits behind the window.</summary>
    Acrylic,
}