using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using DoNet.Models;
using DoNet.ViewModels;

namespace DoNet.Controls;

/// <summary>
/// One record of a designed service type in the records grid.
/// </summary>
/// <remarks>
/// The shell is the XAML; the fields are built in code, from the structure the
/// user designed for the type. Up to four fields show, in definition order - the
/// ones the user put first are the ones they said matter most - and a secret shows
/// as a mask, because a card is for recognising a record, not for reading its
/// passwords off the grid.
///</remarks>
public sealed partial class ServiceRecordCard : UserControl
{
    private const string Dash = "\u2014";
    private const string Mask = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";

    /// <summary>How many of the type's fields preview on the card.</summary>
    private const int PreviewFields = 4;

    public ServiceRecordCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty RecordProperty = DependencyProperty.Register(
        nameof(Record), typeof(ServiceRecord), typeof(ServiceRecordCard),
        new PropertyMetadata(null, OnAnyChanged));

    /// <summary>The record this card shows.</summary>
    public ServiceRecord? Record
    {
        get => (ServiceRecord?)GetValue(RecordProperty);
        set => SetValue(RecordProperty, value);
    }

    public static readonly DependencyProperty StructureProperty = DependencyProperty.Register(
        nameof(Structure), typeof(ServiceDefinition), typeof(ServiceRecordCard),
        new PropertyMetadata(null, OnAnyChanged));

    /// <summary>The structure that says what the record's fields are.</summary>
    public ServiceDefinition? Structure
    {
        get => (ServiceDefinition?)GetValue(StructureProperty);
        set => SetValue(StructureProperty, value);
    }

    public static readonly DependencyProperty ChoicesProperty = DependencyProperty.Register(
        nameof(Choices), typeof(ServiceChoiceOptions), typeof(ServiceRecordCard),
        new PropertyMetadata(null, OnAnyChanged));

    /// <summary>The relationship options, for showing what the record points at.</summary>
    public ServiceChoiceOptions? Choices
    {
        get => (ServiceChoiceOptions?)GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    /// <summary>Double-click: open the full record.</summary>
    public event EventHandler<ServiceRecord>? OpenRequested;

    public event EventHandler<ServiceRecord>? EditRequested;

    public event EventHandler<ServiceRecord>? DeleteRequested;

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ServiceRecordCard)d).Apply();

    private void Apply()
    {
        if (Record is not { } record || Structure is not { } structure)
        {
            return;
        }

        string title = string.IsNullOrWhiteSpace(record.Title)
            ? record.IdDisplay.Length > 0 ? $"Record {record.IdDisplay}" : "Record"
            : record.Title;

        AvatarText.Text = Initial.From(record.Title);
        TitleText.Text = title;
        CaptionText.Text = record.IdDisplay;

        BuildFields(record, structure);
    }

    /// <summary>
    /// Builds the preview fields: two columns, definition order, four at most.
    /// </summary>
    private void BuildFields(ServiceRecord record, ServiceDefinition structure)
    {
        FieldsHost.Children.Clear();
        FieldsHost.RowDefinitions.Clear();
        FieldsHost.ColumnDefinitions.Clear();

        FieldsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FieldsHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        List<ServiceFieldDef> shown = new();

        foreach (ServiceFieldDef field in structure.Fields)
        {
            if (shown.Count >= PreviewFields)
            {
                break;
            }

            shown.Add(field);
        }

        int row = -1;

        for (int i = 0; i < shown.Count; i++)
        {
            if (i % 2 == 0)
            {
                FieldsHost.RowDefinitions.Add(
                    new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });
                row++;
            }

            StackPanel cell = new();

            cell.Children.Add(new FieldLabel
            {
                Text = shown[i].Label.ToUpperInvariant(),
                IconKind = IconFor(shown[i].Kind),
            });

            cell.Children.Add(new TextBlock
            {
                Text = ValueFor(record, shown[i]),
                Style = (Style)Resources["FieldValueStyle"],
            });

            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, i % 2);
            FieldsHost.Children.Add(cell);
        }
    }

    /// <summary>What one field reads as on the card: its value, its target, or a mask.</summary>
    private string ValueFor(ServiceRecord record, ServiceFieldDef field)
    {
        string value = record.Value(field.Key);

        if (field.Kind == ServiceFieldKind.Secret)
        {
            return string.IsNullOrWhiteSpace(value) ? Dash : Mask;
        }

        if (field.Kind is ServiceFieldKind.Person
            or ServiceFieldKind.Website
            or ServiceFieldKind.Account)
        {
            return int.TryParse(value, out int id)
                ? Or(Choices?.For(field.Kind).LabelFor(id))
                : Dash;
        }

        return Or(value);

        static string Or(string value) => string.IsNullOrWhiteSpace(value) ? Dash : value;
    }

    private static string IconFor(ServiceFieldKind kind) => kind switch
    {
        ServiceFieldKind.Text => "Tag",
        ServiceFieldKind.LongText => "FileText",
        ServiceFieldKind.Number => "Hash",
        ServiceFieldKind.Date => "Calendar",
        ServiceFieldKind.Secret => "Lock",
        ServiceFieldKind.Person => "User",
        ServiceFieldKind.Website => "Globe",
        ServiceFieldKind.Account => "Account",
        _ => "Info",
    };

    private bool _pointerOver;
    private bool _focusWithin;
    private bool _actionsShown;

    /// <summary>
    /// Shows or hides the card's three actions.
    /// </summary>
    /// <remarks>
    /// Hit-test invisible while hidden, so an invisible Delete cannot sit over the
    /// record catching clicks meant for the card.
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
    /// Hides the actions, but only once the pointer has really left the card -
    /// PointerExited bubbles.
    /// </summary>
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

    /// <summary>Keeps the actions up while focus moves between them.</summary>
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

    private void OnOpenClick(object sender, RoutedEventArgs args)
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
