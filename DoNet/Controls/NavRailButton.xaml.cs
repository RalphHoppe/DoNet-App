using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Controls;

/// <summary>
/// One circular button in the home screen's left navigation rail.
/// </summary>
/// <remarks>
/// <para>
/// Pointer state is tracked here rather than left to the Button's own visual states,
/// because the Button underneath is only a hit target - it draws nothing. Its states
/// would have no way to reach the disc and icon that sit above it in the Grid.
/// </para>
/// <para>
/// The states are Storyboards begun by name rather than a VisualStateManager.
/// <c>GoToState</c> looks for its state groups on the control's template root, and a
/// UserControl has no template - a well-known way for states to be dropped silently.
/// <c>Begin</c> has no such failure mode.
/// </para>
/// </remarks>
public sealed partial class NavRailButton : UserControl
{
    public static readonly DependencyProperty IconDataProperty = DependencyProperty.Register(
        nameof(IconData), typeof(Geometry), typeof(NavRailButton), new PropertyMetadata(null));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(NavRailButton), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(NavRailButton), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected), typeof(bool), typeof(NavRailButton),
        new PropertyMetadata(false, OnIsSelectedChanged));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(NavRailButton), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter), typeof(object), typeof(NavRailButton), new PropertyMetadata(null));

    private bool _isOver;
    private bool _isPressed;

    public NavRailButton()
    {
        InitializeComponent();

        // Storyboards cannot target elements before the tree is live, so the initial
        // selection is applied on Loaded - and skipped straight to its end value, so a
        // button that starts selected is simply drawn that way rather than animating in.
        Loaded += (_, _) => ApplySelectionState(animate: false);
    }

    /// <summary>The icon outline, as path mini-language in markup.</summary>
    public Geometry? IconData
    {
        get => (Geometry?)GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    /// <summary>Tooltip text, and the name screen readers announce.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Disc colour when selected. Must be set; there is no sensible default.</summary>
    public Brush? AccentBrush
    {
        get => (Brush?)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    private static void OnIsSelectedChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((NavRailButton)sender).ApplySelectionState(animate: true);
    }

    private void ApplySelectionState(bool animate) =>
        Play(IsSelected ? "ToSelected" : "ToUnselected", animate);

    private void ApplyCommonState() =>
        Play(_isPressed ? "ToPressed" : _isOver ? "ToHover" : "ToNormal", animate: true);

    /// <summary>
    /// Runs one of the state storyboards, optionally jumping straight to its end.
    /// </summary>
    /// <remarks>
    /// The previous storyboard is deliberately not stopped. A finished Storyboard only
    /// *holds* the value it animated to - stopping it would snap the property back to
    /// what it was before, so the disc would flash white between states. Beginning the
    /// next one takes over the property cleanly.
    /// </remarks>
    private void Play(string key, bool animate)
    {
        if (Resources[key] is not Storyboard board)
        {
            return;
        }

        board.Begin();

        if (!animate)
        {
            board.SkipToFill();
        }
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _isOver = true;
        ApplyCommonState();
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs args)
    {
        // Also wired to PointerCanceled and PointerCaptureLost: without those, dragging
        // off the button mid-press would leave it stuck looking pressed.
        _isOver = false;
        _isPressed = false;
        ApplyCommonState();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        _isPressed = true;
        ApplyCommonState();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs args)
    {
        _isPressed = false;
        ApplyCommonState();
    }
}
