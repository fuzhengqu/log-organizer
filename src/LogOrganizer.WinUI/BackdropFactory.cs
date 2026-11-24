using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using LogOrganizer.Core;
using Windows.UI.ViewManagement;

namespace LogOrganizer.WinUI;

/// <summary>One place for backdrop support checks, material creation and the matching window surface.</summary>
internal static class BackdropFactory
{
    /// <summary>Falls back to a solid surface when the requested material is unavailable.</summary>
    internal static BackdropMode Resolve(BackdropMode requested)
    {
        if (requested == BackdropMode.Solid || IsHighContrast())
        {
            return BackdropMode.Solid;
        }

        return requested switch
        {
            BackdropMode.Mica when MicaController.IsSupported() => BackdropMode.Mica,
            BackdropMode.Acrylic when DesktopAcrylicController.IsSupported() => BackdropMode.Acrylic,
            _ => BackdropMode.Solid,
        };
    }

    internal static SystemBackdrop? Create(BackdropMode resolved) => resolved switch
    {
        // Base keeps more of the system tint, so text stays readable over busy wallpapers.
        BackdropMode.Mica => new MicaBackdrop { Kind = MicaKind.Base },
        BackdropMode.Acrylic => new DesktopAcrylicBackdrop(),
        _ => null,
    };

    /// <summary>Transparent for system materials so the backdrop shows through; opaque for solid mode.</summary>
    internal static Brush SurfaceBrush(BackdropMode resolved) => (Brush)Application.Current.Resources[
        resolved == BackdropMode.Solid ? "AppSolidSurfaceBrush" : "AppTransparentBrush"];

    private static bool IsHighContrast()
    {
        try
        {
            return new AccessibilitySettings().HighContrast;
        }
        catch (Exception)
        {
            return false;
        }
    }
}