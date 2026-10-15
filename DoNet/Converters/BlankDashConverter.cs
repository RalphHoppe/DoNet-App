using System;
using Microsoft.UI.Xaml.Data;

namespace DoNet.Converters;

/// <summary>
/// Renders an empty value as the em-dash the design uses for "nothing here".
/// </summary>
/// <remarks>
/// An em-dash, not a hyphen: at 14px the difference is visible and the design uses the
/// long one. Centralised so every field cannot drift to its own placeholder.
/// </remarks>
public sealed partial class BlankDashConverter : IValueConverter
{
    public const string Dash = "\u2014";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string? text = value?.ToString();
        return string.IsNullOrWhiteSpace(text) ? Dash : text;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
