using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DoNet.Controls;

/// <summary>
/// One circular button in the home screen's left navigation rail.
/// </summary>
/// <remarks>
/// Pointer state is tracked here rather than left to the Button's own visual states,
/// because the Button underneath is only a hit target - it draws nothing. Its states
/// would have no way to reach the disc and icon that sit above it in the Grid.
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

        // Visual states do not stick before the tree is live, so the initial selection
        // is applied on Loaded - without transitions, so a button that starts selected
        // is simply drawn that way instead of animating into it.
        Loaded += (_, _) => ApplySelectionState(useTransitions: false);
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
        ((NavRailButton)sender).ApplySelectionState(useTransitions: true);
    }

    private void ApplySelectionState(bool useTransitions) =>
        VisualStateManager.GoToState(this, IsSelected ? "Selected" : "Unselected", useTransitions);

    private void ApplyCommonState() =>
        VisualStateManager.GoToState(
            this,
            _isPressed ? "Pressed" : _isOver ? "PointerOver" : "Normal",
            useTransitions: true);

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
