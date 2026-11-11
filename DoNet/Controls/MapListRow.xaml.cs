using System;
using DoNet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DoNet.Controls;

/// <summary>
/// One row of the map panel's list. See the XAML for how the row carries
/// the record's flag, name, country and place in the network.
/// </summary>
public sealed partial class MapListRow : UserControl
{
    /// <summary>Identifies the <see cref="Model"/> property.</summary>
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(MapRowItem), typeof(MapListRow),
        new PropertyMetadata(null, OnModelChanged));

    public MapListRow()
    {
        InitializeComponent();

        // The states do not drive themselves inside a UserControl: the
        // pointer's arrival and leaving are forwarded by hand.
        PointerEntered += (_, _) =>
        {
            _ = VisualStateManager.GoToState(this, "PointerOver", useTransitions: true);
            if (Model is { } model)
            {
                RowPointerEntered?.Invoke(this, model);
            }
        };
        PointerExited += (_, _) =>
        {
            _ = VisualStateManager.GoToState(this, "Rest", useTransitions: true);
            if (Model is { } model)
            {
                RowPointerExited?.Invoke(this, model);
            }
        };
    }

    /// <summary>The row's data: the record it speaks for.</summary>
    public MapRowItem? Model
    {
        get => (MapRowItem?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    /// <summary>The row was clicked: the view decides what that means.</summary>
    public event EventHandler<MapRowItem>? RowTapped;

    /// <summary>The pointer arrived on the row: its marker should stand up.</summary>
    public event EventHandler<MapRowItem>? RowPointerEntered;

    /// <summary>The pointer left the row: its marker should settle.</summary>
    public event EventHandler<MapRowItem>? RowPointerExited;

    /// <summary>
    /// The row's button was clicked: the tap is forwarded with the row's
    /// model, and nothing else happens here.
    /// </summary>
    private void OnClick(object sender, RoutedEventArgs args)
    {
        if (Model is { } model)
        {
            RowTapped?.Invoke(this, model);
        }
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MapListRow)d).Apply();

    /// <summary>
    /// Fills the row from its model: the flag (or the globe), and the row's
    /// step by its depth in the chain.
    /// </summary>
    private void Apply()
    {
        if (Model is not { } model)
        {
            return;
        }

        Mark.Content = model.FlagCountry is { } country
            ? new FlagIcon { Country = country }
            : new LineIcon
            {
                Kind = "Globe",
                Width = 16,
                Height = 16,
                Foreground = (Brush)Application.Current.Resources["CardMetaBrush"],
            };

        // The chain reads as the chain it is: each level steps in from its
        // parent's left edge.
        Padding = new Thickness(11 + (model.Level * 13), 5, 9, 5);
    }
}
