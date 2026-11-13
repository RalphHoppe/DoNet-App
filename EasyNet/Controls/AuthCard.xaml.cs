using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace EasyNet.Controls;

/// <summary>
/// The floating card every entry screen lives on. See the XAML for how it is
/// drawn and how it reflows; this carries the form it hosts and chooses the
/// posture the width asks for.
/// </summary>
/// <remarks>
/// The card does not size itself to the window - it centres when there is
/// room and scrolls when there is not, so the smallest window still shows
/// the whole form. What the width does decide is posture: brand beside the
/// form when the card can be its full width, brand above it when it cannot.
/// The threshold is the page's own width rather than the card's, because
/// the card stops growing at its cap and would never report the narrow
/// widths below it.
///</remarks>
public sealed partial class AuthCard : UserControl
{
    /// <summary>
    /// The width at which the brand moves from above the form to beside it:
    /// the card's cap plus the page's breathing margin around it.
    /// </summary>
    private const double WideStateMinimumWidth = 700;

    /// <summary>Identifies the <see cref="FormContent"/> property.</summary>
    public static readonly DependencyProperty FormContentProperty = DependencyProperty.Register(
        nameof(FormContent), typeof(UIElement), typeof(AuthCard), new PropertyMetadata(null));

    public AuthCard()
    {
        InitializeComponent();

        SizeChanged += (_, _) => ApplyPosture();
    }

    /// <summary>The form this card serves - handed in by the page that owns it.</summary>
    public UIElement? FormContent
    {
        get => (UIElement?)GetValue(FormContentProperty);
        set => SetValue(FormContentProperty, value);
    }

    /// <summary>
    /// The posture the width asks for. Runs on the first layout too, so the
    /// card is never seen in the wrong one.
    /// </summary>
    private void ApplyPosture() =>
        _ = VisualStateManager.GoToState(
            this,
            ActualWidth >= WideStateMinimumWidth ? "WideState" : "NarrowState",
            useTransitions: false);
}
