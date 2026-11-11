using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DoNet.Converters;

/// <summary>
/// Maps <see cref="bool"/> to <see cref="Visibility"/>.
/// Pass <c>Invert</c> as the converter parameter to flip the result.
/// </summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is bool b && b;

        if (IsInverted(parameter))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var visible = value is Visibility visibility && visibility == Visibility.Visible;

        return IsInverted(parameter) ? !visible : visible;
    }

    private static bool IsInverted(object parameter) =>
        parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase);
}
