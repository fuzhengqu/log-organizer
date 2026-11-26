using Microsoft.UI.Xaml.Data;

namespace LogOrganizer.WinUI;

/// <summary>
/// Maps a toggle's checked state onto the pin glyph's rotation. The pin is drawn diagonal and turns
/// upright to read as "pinned in".
/// </summary>
/// <remarks>
/// The angle is bound rather than assigned from a visual state on purpose. A <c>VisualState</c>
/// setter that hands <see cref="Microsoft.UI.Xaml.Media.RotateTransform"/> a brand-new object from a
/// style in <c>App.xaml</c> never materialises, so the pinned state used to leave the glyph untouched
/// and the icon appeared stuck.
/// </remarks>
public sealed partial class PinAngleConverter : IValueConverter
{
    private const double UnpinnedAngle = -45;
    private const double PinnedAngle = 0;

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? PinnedAngle : UnpinnedAngle;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}