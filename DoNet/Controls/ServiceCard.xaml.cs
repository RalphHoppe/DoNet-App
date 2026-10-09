using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using DoNet.Models;

namespace DoNet.Controls;

/// <summary>
/// One service type in the directory grid.
/// </summary>
public sealed partial class ServiceCard : UserControl
{
    private const string Dash = "\u2014";

    public ServiceCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ServiceProperty = DependencyProperty.Register(
        nameof(Service), typeof(Service), typeof(ServiceCard),
        new PropertyMetadata(null, OnServiceChanged));

    /// <summary>The record this card shows.</summary>
    public Service? Service
    {
        get => (Service?)GetValue(ServiceProperty);
        set => SetValue(ServiceProperty, value);
    }

    /// <summary>Double-click: open the full record.</summary>
    public event EventHandler<Service>? OpenRequested;

    public event EventHandler<Service>? EditRequested;

    public event EventHandler<Service>? DeleteRequested;

    private static void OnServiceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ServiceCard)d).Apply();

    /// <summary>
    /// Pushes the record into the card.
    /// </summary>
    /// <remarks>
    /// Assigned in code rather than by binding because <see cref="Service"/> is a
    /// plain mutable class with no change notification - the same arrangement as
    /// every other card, for the same reason.
    /// </remarks>
    private void Apply()
    {
        if (Service is not { } service)
        {
            return;
        }

        AvatarText.Text = Initial.From(service.Name);
        NameText.Text = service.DisplayName;

        DescriptionValue.Text = Or(service.Description);
        NoteValue.Text = Or(service.Note);

        static string Or(string value) => string.IsNullOrWhiteSpace(value) ? Dash : value;
    }

    private bool _pointerOver;
    private bool _focusWithin;
    private bool _actionsShown;

    /// <summary>
    /// Shows or hides the card's two actions.
    /// </summary>
    /// <remarks>
    /// They are hit-test invisible while hidden. They stay in the tree at zero
    /// opacity, and without this an invisible Delete would sit over the record
    /// catching clicks meant for the card.
    /// </remarks>
    private void SetActions(bool shown)
    {
        if (_actionsShown == shown)
        {
            return;
        }

        _actionsShown = shown;
        ActionLayer.IsHitTestVisible = shown;

        if (Resources[shown ? "ActionsShow" : "ActionsHide"] is Storyboard board)
        {
            board.Begin();
        }
    }

    private void ApplyActionState() => SetActions(_pointerOver || _focusWithin);

    private void HideActions()
    {
        _pointerOver = false;
        _focusWithin = false;
        ApplyActionState();
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        _pointerOver = true;
        ApplyActionState();
    }

    /// <summary>
    /// Hides the actions, but only once the pointer has really left the card.
    /// </summary>
    /// <remarks>
    /// PointerExited bubbles. Moving off one of the action buttons and back onto the
    /// card raises it on the card as well, so without the bounds check the actions
    /// would blink out every time the pointer crossed one of them - which is every
    /// time somebody reaches for them.
    /// </remarks>
    private void OnCardPointerExited(object sender, PointerRoutedEventArgs args)
    {
        Point point = args.GetCurrentPoint(Shell).Position;

        if (point.X >= 0 && point.Y >= 0 &&
            point.X <= Shell.ActualWidth && point.Y <= Shell.ActualHeight)
        {
            return;
        }

        _pointerOver = false;
        ApplyActionState();
    }

    private void OnActionGotFocus(object sender, RoutedEventArgs args)
    {
        _focusWithin = true;
        ApplyActionState();
    }

    /// <summary>
    /// Keeps the actions up while focus moves between them.
    /// </summary>
    /// <remarks>
    /// Tabbing from Edit to Delete raises LostFocus before the other's GotFocus, so
    /// deciding immediately would hide the buttons underneath the caret. Queuing the
    /// decision lets both events land first.
    /// </remarks>
    private void OnActionLostFocus(object sender, RoutedEventArgs args)
    {
        _focusWithin = false;
        DispatcherQueue.TryEnqueue(ApplyActionState);
    }

    private void OnActionPointerEntered(object sender, PointerRoutedEventArgs args)
    {
        if (sender is Button button)
        {
            button.Opacity = 0.86;
        }
    }

    private void OnActionPointerExited(object sender, PointerRoutedEventArgs args)
    {
        if (sender is Button button)
        {
            button.Opacity = 1;
        }
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        HideActions();

        if (Service is { } service)
        {
            OpenRequested?.Invoke(this, service);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs args)
    {
        HideActions();

        if (Service is { } service)
        {
            EditRequested?.Invoke(this, service);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        HideActions();

        if (Service is { } service)
        {
            DeleteRequested?.Invoke(this, service);
        }
    }
}
