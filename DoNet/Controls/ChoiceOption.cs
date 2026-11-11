using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace DoNet.Controls;

/// <summary>
/// One row in a <see cref="ChoicePicker"/>'s drawer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Label"/> is what the row reads and <see cref="Result"/> is what the
/// field is set to. They are the same for every row drawn from the option list, and
/// differ for the one the picker offers when the typed text matches nothing: that row
/// reads <c>Use "Andorra la Vella"</c> and commits the text itself.
/// </para>
/// <para>
/// The visibilities are properties rather than converter bindings. There are three of
/// them on every row of a list that can be two hundred long, and a property read is
/// cheaper than a converter call on each.
/// </para>
/// </remarks>
public sealed partial class ChoiceOption : ObservableObject
{
    /// <summary>Creates a row.</summary>
    /// <param name="label">What the row reads.</param>
    /// <param name="result">What picking it sets the field to.</param>
    /// <param name="country">A country whose flag to draw, or null for no flag.</param>
    /// <param name="iconKind">A line icon to draw, or null for none.</param>
    public ChoiceOption(string label, string result, string? country, string? iconKind)
    {
        Label = label;
        Result = result;
        Country = country;
        IconKind = iconKind;
    }

    /// <summary>What the row reads.</summary>
    public string Label { get; }

    /// <summary>What picking this row sets the field to.</summary>
    public string Result { get; }

    /// <summary>The country whose flag leads the row, if any.</summary>
    public string? Country { get; }

    /// <summary>The line icon that leads the row, if there is no flag.</summary>
    public string? IconKind { get; }

    /// <summary>Whether this row draws a flag.</summary>
    public Visibility HasFlag => Country is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Whether this row draws a line icon.</summary>
    public Visibility HasIcon => IconKind is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Whether the tick is shown, because this row is the current value.</summary>
    [ObservableProperty]
    private Visibility _isChosen = Visibility.Collapsed;
}
