using System;
using System.Globalization;
using DoNet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace DoNet.Controls;

/// <summary>
/// One VPS record in the directory grid.
/// </summary>
public sealed partial class VpsCard : UserControl
{
    private const string Dash = "\u2014";

    private bool _pointerOver;
    private bool _focusWithin;
    private bool _actionsShown;

    public VpsCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record), typeof(VpsRecord), typeof(VpsCard),
        new PropertyMetadata(null, OnRecordChanged));

    /// <summary>The record this card shows.</summary>
    public VpsRecord? Record
    {
        get => (VpsRecord?)GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    /// <summary>Double-click: open the full record.</summary>
    public event EventHandler<VpsRecord>? OpenRequested;

    public event EventHandler<VpsRecord>? EditRequested;

    public event EventHandler<VpsRecord>? DeleteRequested;

    private static void OnRecordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((VpsCard)d).Apply();

    /// <summary>
    /// Pushes the record into the card, formats and all.
    /// </summary>
    /// <remarks>
    /// Assigned in code rather than by binding for the reason the other cards do
    /// it: the record is a plain mutable class refreshed by reassignment, not one
    /// that raises changes.
    /// </remarks>
    private void Apply()
    {
        if (Record is not { } record)
        {
            return;
        }

        NameText.Text = string.IsNullOrWhiteSpace(record.Name) ? "VPS record" : record.Name;

        // The node voice: a record either is a main or belongs to one.
        CaptionText.Text = record.IsMain
            ? "Main VPS"
            : string.IsNullOrWhiteSpace(record.NodeName)
                ? $"Node · #{record.NodeVpsId}"
                : $"Node of {record.NodeName}";

        SystemValue.Text = record.System is { } system
            ? Join(system.Os, system.Ip)
            : Dash;

        BalanceValue.Text = record.Balance is { } balance
            ? Join(
                $"{balance.ToString("0.########", CultureInfo.InvariantCulture)} {record.Currency}",
                record.BalanceBtc is { } bitcoin
                    ? $"{bitcoin.ToString("0.########", CultureInfo.InvariantCulture)} BTC"
                    : string.Empty)
            : Dash;

        SubscriptionValue.Text = Join(
            record.CycleLabel,
            string.IsNullOrWhiteSpace(record.OutDate) ? string.Empty : $"out {record.OutDate}");

        CountryValue.Text = Or(record.Country);
        NoteValue.Text = Or(record.Note);

        static string Or(string value) => string.IsNullOrWhiteSpace(value) ? Dash : value;
    }

    /// <summary>Two halves with a dot between; a dash when both are missing.</summary>
    private static string Join(string first, string second)
    {
        bool hasFirst = !string.IsNullOrWhiteSpace(first);
        bool hasSecond = !string.IsNullOrWhiteSpace(second);

        return (hasFirst, hasSecond) switch
        {
            (true, true) => $"{first} · {second}",
            (true, false) => first,
            (false, true) => second,
            _ => Dash,
        };
    }

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
    /// would blink out every time the pointer crossed one of them.
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

        if (Record is { } record)
        {
            OpenRequested?.Invoke(this, record);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs args)
    {
        HideActions();

        if (Record is { } record)
        {
            EditRequested?.Invoke(this, record);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        HideActions();

        if (Record is { } record)
        {
            DeleteRequested?.Invoke(this, record);
        }
    }
}
