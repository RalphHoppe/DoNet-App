using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Controls;

/// <summary>
/// The uppercase caption above a field, with the icon that belongs to it.
/// </summary>
public sealed partial class FieldLabel : UserControl
{
    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FieldLabel),
        new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>Identifies the <see cref="IconKind"/> property.</summary>
    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind), typeof(string), typeof(FieldLabel),
        new PropertyMetadata(null, OnIconKindChanged));

    /// <summary>Creates the control.</summary>
    public FieldLabel() => InitializeComponent();

    /// <summary>The caption itself.</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Which icon to show, named as in <see cref="LineIconData.Names"/>. Leave it
    /// unset for a caption with no mark.
    /// </summary>
    public string? IconKind
    {
        get => (string?)GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((FieldLabel)sender).Caption.Text = (string)args.NewValue;

    private static void OnIconKindChanged(
        DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        FieldLabel label = (FieldLabel)sender;
        string? kind = (string?)args.NewValue;

        label.Icon.Kind = kind;

        // Collapsed rather than blank, so a caption with no icon starts at the same
        // place as the text in one that has none at all.
        label.Icon.Visibility = string.IsNullOrEmpty(kind) ? Visibility.Collapsed : Visibility.Visible;
    }
}
