using System;
using System.Globalization;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using DoNet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Controls;

/// <summary>
/// One person in the directory grid.
/// </summary>
public sealed partial class WebsiteCard : UserControl
{
    private const string Dash = "\u2014";

    public WebsiteCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty WebsiteProperty = DependencyProperty.Register(
        nameof(Website), typeof(Website), typeof(WebsiteCard),
        new PropertyMetadata(null, OnWebsiteChanged));

    /// <summary>The record this card shows.</summary>
    public Website? Website
    {
        get => (Website?)GetValue(WebsiteProperty);
        set => SetValue(WebsiteProperty, value);
    }

    /// <summary>Double-click: open the full record.</summary>
    public event EventHandler<Website>? OpenRequested;

    public event EventHandler<Website>? EditRequested;

    public event EventHandler<Website>? DeleteRequested;

    private static void OnWebsiteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((WebsiteCard)d).Apply();

    /// <summary>
    /// Pushes the record into the card.
    /// </summary>
    /// <remarks>
    /// Assigned in code rather than by binding because <see cref="Website"/> is a plain
    /// mutable class with no change notification - it is edited through a copy and
    /// committed, so the card is told to refresh rather than watching each property.
    /// </remarks>
    private void Apply()
    {
        Website? website = Website;

        if (website is null)
        {
            return;
        }

        // The record number, not an initial. The approved design puts the id in the
        // disc; it is the one field that is always present and always unique.
        AvatarText.Text = website.Id > 0 ? website.Id.ToString(CultureInfo.InvariantCulture) : "-";
        NameText.Text = website.DisplayName;

        NameValue.Text = Or(website.Name);
        DomainValue.Text = Or(website.Domain);
        DescriptionValue.Text = Or(website.Description);
        NoteValue.Text = Or(website.Note);

        BuildChips(website.PaymentMethods);

        static string Or(string value) => string.IsNullOrWhiteSpace(value) ? Dash : value;
    }

    /// <summary>
    /// Rebuilds the payment-method chips.
    /// </summary>
    /// <remarks>
    /// Built in code rather than by an ItemsRepeater because a card shows at most a
    /// handful and they never change while it is on screen - a repeater would add a
    /// layout, a template and a view-model per chip to animate nothing.
    ///
    /// When there are none the dash shows instead, so an empty methods cell reads the
    /// same as every other empty field on the card.
    /// </remarks>
    private void BuildChips(IReadOnlyList<string> methods)
    {
        MethodChips.Children.Clear();

        bool any = methods.Count > 0;

        NoMethodsValue.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        MethodChips.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        if (!any)
        {
            NoMethodsValue.Text = Dash;
            return;
        }

        foreach (string method in methods)
        {
            MethodChips.Children.Add(BuildChip(method));
        }
    }

    private static Border BuildChip(string method)
    {
        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            VerticalAlignment = VerticalAlignment.Center,
        };

        content.Children.Add(new PaymentMethodIcon
        {
            Method = method,
            Width = 14,
            Height = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });

        content.Children.Add(new TextBlock
        {
            Text = method,
            FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["CardMetaBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        return new Border
        {
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(8, 3, 10, 3),
            Background = (Brush)Application.Current.Resources["CardAvatarBrush"],
            BorderBrush = (Brush)Application.Current.Resources["SearchBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private bool _menuOpen;

    /// <summary>
    /// Opens or closes the drawer.
    /// </summary>
    /// <remarks>
    /// The items are hit-test invisible while closed. They are still in the tree with
    /// zero opacity, and without this an invisible Delete would sit over the card's
    /// first field, catching clicks aimed at the record.
    /// </remarks>
    private void SetMenu(bool open)
    {
        if (_menuOpen == open)
        {
            return;
        }

        _menuOpen = open;

        EditItem.IsHitTestVisible = open;
        DeleteItem.IsHitTestVisible = open;

        if (Resources[open ? "MenuOpen" : "MenuClose"] is Storyboard board)
        {
            board.Begin();
        }
    }

    private void OnMenuToggle(object sender, RoutedEventArgs args) => SetMenu(!_menuOpen);

    /// <summary>
    /// Light dismiss. A drawer attached to the card closes when the pointer leaves the
    /// card, which is the gesture people already make when they change their mind.
    /// </summary>
    private void OnCardPointerExited(object sender, PointerRoutedEventArgs args)
        => SetMenu(false);

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        SetMenu(false);

        if (Website is { } website)
        {
            OpenRequested?.Invoke(this, website);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs args)
    {
        SetMenu(false);

        if (Website is { } website)
        {
            EditRequested?.Invoke(this, website);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        SetMenu(false);

        if (Website is { } website)
        {
            DeleteRequested?.Invoke(this, website);
        }
    }
}
