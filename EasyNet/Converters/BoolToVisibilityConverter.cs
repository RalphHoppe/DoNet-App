using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace EasyNet.Converters;

/// <summary>
/// Turns a view model's true and false into the visible and the collapsed,
/// for the hints that arrive only when they are being earned.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object value, System.Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    /// <remarks>One direction is enough: nothing here binds visibility back.</remarks>
    public object ConvertBack(object value, System.Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}
