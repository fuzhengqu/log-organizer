using Microsoft.UI.Xaml.Data;

namespace LogOrganizer.WinUI;

/// <summary>
/// Maps a toggle's checked state onto the pin glyph's rotation. Segoe draws the pin diagonally, and
/// Windows stands it upright for a pinned window, so the checked state is the one that rotates.
/// </summary>
/// <remarks>
/// The angle is bound rather than assigned from a visual state on purpose. A <c>VisualState</c>
/// setter that hands <see cref="Microsoft.UI.Xaml.Media.RotateTransform"/> a brand-new object from a
/// style in <c>App.xaml</c> never materialises, so the pinned state used to leave the glyph untouched
/// and the icon appeared stuck.
/// </remarks>
public sealed partial class PinAngleConverter : IValueConverter
{
    // Segoe's pin glyph is drawn diagonally at 0°. Rotating it -45° stands it upright, which is the
    // look Windows gives a pinned window, so the checked state gets the rotation.
    private const double UnpinnedAngle = 0;
    private const double PinnedAngle = -45;

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? PinnedAngle : UnpinnedAngle;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}