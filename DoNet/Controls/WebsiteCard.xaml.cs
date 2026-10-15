using System;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using DoNet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

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

        // The panel works out how many chips fit during layout, so the count arrives
        // after the fact rather than being something this card can calculate.
        MethodChips.RegisterPropertyChangedCallback(
            WrapPanel.HiddenCountProperty, (_, _) => OnHiddenCountChanged());
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
        AvatarText.Text = Initial.From(website.Name, website.Domain);
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

        // The marker is declared in XAML but Clear detached it, and the panel only
        // treats the final child as the overflow marker - so it goes back last.
        MethodChips.Children.Add(MoreMethods);
    }

    private void OnHiddenCountChanged()
    {
        int hidden = MethodChips.HiddenCount;
        MoreMethods.Text = hidden > 0 ? $"+{hidden}" : string.Empty;
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

        if (Website is { } website)
        {
            OpenRequested?.Invoke(this, website);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs args)
    {
        HideActions();

        if (Website is { } website)
        {
            EditRequested?.Invoke(this, website);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        HideActions();

        if (Website is { } website)
        {
            DeleteRequested?.Invoke(this, website);
        }
    }
}
