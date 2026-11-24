using LogOrganizer.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace LogOrganizer.WinUI;

/// <summary>Shared title-bar geometry and caption colours so both windows look like one app.</summary>
internal static class WindowChrome
{
    /// <summary>Three system caption buttons at 100% scale, used when the OS reports no inset.</summary>
    internal const double FallbackCaptionInsetDip = 138;

    /// <summary>Same hit area as a system caption button.</summary>
    internal const double CaptionButtonWidthDip = 46;

    /// <summary>
    /// Width the system caption buttons reserve on the right edge, in DIP. Windows 10 and early
    /// frames report 0, so a measured fallback keeps layout sane instead of leaving a 0 gap.
    /// </summary>
    internal static double CaptionInsetDip(Window window, XamlRoot? xamlRoot)
    {
        var scale = xamlRoot?.RasterizationScale ?? 1.0;
        if (scale <= 0)
        {
            scale = 1.0;
        }

        var inset = window.AppWindow.TitleBar.RightInset / scale;
        return inset > 0 ? inset : FallbackCaptionInsetDip;
    }

    /// <summary>
    /// Resizes by DIP so the layout keeps its proportions on 125%/150% displays. AppWindow.Resize
    /// takes physical pixels, which would shrink the window to 2/3 of the design size at 150%.
    /// Height is clamped to the work area so the footer buttons stay reachable on small screens.
    /// </summary>
    internal static void ResizeDip(Window window, double widthDip, double heightDip, Window? reference = null)
    {
        var scale = NativeMethods.DpiScale(WindowNative.GetWindowHandle(window));
        var height = (int)Math.Round(heightDip * scale);
        var display = DisplayArea.GetFromWindowId(
            (reference ?? window).AppWindow.Id, DisplayAreaFallback.Nearest);
        if (display is not null)
        {
            var margin = (int)Math.Round(32 * scale);
            height = Math.Min(height, display.WorkArea.Height - margin);
        }

        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)Math.Round(widthDip * scale), height));
    }

    /// <summary>Places <paramref name="child"/> over the centre of <paramref name="owner"/>, kept on screen.</summary>
    internal static void CenterOver(Window owner, Window child)
    {
        var display = DisplayArea.GetFromWindowId(owner.AppWindow.Id, DisplayAreaFallback.Nearest);
        if (display is null)
        {
            return;
        }

        var area = display.WorkArea;
        var origin = owner.AppWindow.Position;
        var ownerSize = owner.AppWindow.Size;
        var size = child.AppWindow.Size;
        var x = origin.X + (ownerSize.Width - size.Width) / 2;
        var y = origin.Y + (ownerSize.Height - size.Height) / 2;
        x = area.Width > size.Width ? Math.Clamp(x, area.X, area.X + area.Width - size.Width) : area.X;
        y = area.Height > size.Height ? Math.Clamp(y, area.Y, area.Y + area.Height - size.Height) : area.Y;
        child.AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
    }

    internal static BackdropMode ApplyBackdrop(Window window, Panel root, BackdropMode requested)
    {
        var resolved = BackdropFactory.Resolve(requested);
        window.SystemBackdrop = BackdropFactory.Create(resolved);
        root.Background = BackdropFactory.SurfaceBrush(resolved);
        ApplyCaptionColors(window, root, resolved != BackdropMode.Solid);
        return resolved;
    }

    /// <summary>Blends the system caption buttons with the material instead of leaving opaque squares.</summary>
    internal static void ApplyCaptionColors(Window window, Panel root, bool translucent)
    {
        var titleBar = window.AppWindow.TitleBar;
        if (!translucent)
        {
            titleBar.ButtonBackgroundColor = null;
            titleBar.ButtonInactiveBackgroundColor = null;
            titleBar.ButtonHoverBackgroundColor = null;
            titleBar.ButtonPressedBackgroundColor = null;
            titleBar.ButtonForegroundColor = null;
            titleBar.ButtonInactiveForegroundColor = null;
            titleBar.ButtonHoverForegroundColor = null;
            titleBar.ButtonPressedForegroundColor = null;
            return;
        }

        var dark = root.ActualTheme == ElementTheme.Dark;
        var transparent = Windows.UI.Color.FromArgb(0x00, 0x00, 0x00, 0x00);
        var hover = dark ? Windows.UI.Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x14, 0x00, 0x00, 0x00);
        var pressed = dark ? Windows.UI.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x24, 0x00, 0x00, 0x00);
        var foreground = dark ? Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0xE6, 0x00, 0x00, 0x00);
        var inactive = dark ? Windows.UI.Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x99, 0x00, 0x00, 0x00);

        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}