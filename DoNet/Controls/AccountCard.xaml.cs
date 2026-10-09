using System;
using DoNet.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace DoNet.Controls;

/// <summary>
/// One account in the directory grid.
/// </summary>
public sealed partial class AccountCard : UserControl
{
    private const string Dash = "\u2014";

    private bool _pointerOver;
    private bool _focusWithin;
    private bool _actionsShown;

    public AccountCard()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty AccountProperty = DependencyProperty.Register(
        nameof(Account), typeof(Account), typeof(AccountCard),
        new PropertyMetadata(null, OnAccountChanged));

    /// <summary>The record this card shows.</summary>
    public Account? Account
    {
        get => (Account?)GetValue(AccountProperty);
        set => SetValue(AccountProperty, value);
    }

    /// <summary>Double-click: open the full record.</summary>
    public event EventHandler<Account>? OpenRequested;

    public event EventHandler<Account>? EditRequested;

    public event EventHandler<Account>? DeleteRequested;

    private static void OnAccountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AccountCard)d).Apply();

    /// <summary>
    /// Pushes the record into the card.
    /// </summary>
    /// <remarks>
    /// Assigned in code rather than by binding because <see cref="Account"/> is a
    /// plain mutable class with no change notification - it is edited through a copy
    /// and committed, so the card is told to refresh rather than watching each
    /// property.
    /// </remarks>
    private void Apply()
    {
        Account? account = Account;

        if (account is null)
        {
            return;
        }

        // The username first, the site second: an account with no username still has
        // a site, so the disc is never empty.
        AvatarText.Text = Initial.From(account.Username, account.WebsiteDisplay);
        NameText.Text = account.DisplayName;

        WebsiteValue.Text = Or(account.WebsiteDisplay);
        UsernameValue.Text = Or(account.Username);
        NoteValue.Text = Or(account.Note);

        static string Or(string value) => string.IsNullOrWhiteSpace(value) ? Dash : value;
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

        if (Account is { } account)
        {
            OpenRequested?.Invoke(this, account);
        }
    }

    private void OnEditClick(object sender, RoutedEventArgs args)
    {
        HideActions();

        if (Account is { } account)
        {
            EditRequested?.Invoke(this, account);
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        HideActions();

        if (Account is { } account)
        {
            DeleteRequested?.Invoke(this, account);
        }
    }
}
