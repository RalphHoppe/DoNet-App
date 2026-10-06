using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DoNet.Controls;

/// <summary>
/// A password input styled to the DoNet design: a rounded 38px field with an inline
/// padlock, none of the stock WinUI chrome, and a border that reacts to hover, focus
/// and validation.
/// </summary>
public sealed partial class PasswordField : UserControl
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.Register(
        nameof(Password),
        typeof(string),
        typeof(PasswordField),
        new PropertyMetadata(string.Empty, OnPasswordPropertyChanged));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText),
        typeof(string),
        typeof(PasswordField),
        new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(
        nameof(HasError),
        typeof(bool),
        typeof(PasswordField),
        new PropertyMetadata(false, OnHasErrorChanged));

    public static readonly DependencyProperty ShowRevealButtonProperty = DependencyProperty.Register(
        nameof(ShowRevealButton),
        typeof(bool),
        typeof(PasswordField),
        new PropertyMetadata(false, OnShowRevealButtonChanged));

    /// <summary>Guards the two-way sync between the DP and the inner PasswordBox.</summary>
    private bool _syncing;
    private bool _isFocused;
    private bool _isPointerOver;
    private bool _isRevealed;

    public PasswordField()
    {
        InitializeComponent();
        ApplyRevealState();

        FieldBorder.PointerEntered += (_, _) =>
        {
            _isPointerOver = true;
            UpdateVisualState();
        };

        FieldBorder.PointerExited += (_, _) =>
        {
            _isPointerOver = false;
            UpdateVisualState();
        };
    }

    /// <summary>Raised when the user presses Enter inside the field.</summary>
    public event EventHandler? Submitted;

    /// <summary>The typed password. Two-way bindable.</summary>
    public string Password
    {
        get => (string)GetValue(PasswordProperty);
        set => SetValue(PasswordProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Turns the border red. Set by the view model after failed validation.</summary>
    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
    }

    /// <summary>
    /// Shows the eye toggle at the right of the field.
    /// </summary>
    /// <remarks>
    /// Off by default. The lock screen offers it because a mistyped password there is a
    /// dead end; the create-password screen does not, because it already asks for the
    /// password twice and the design shows no eye.
    /// </remarks>
    public bool ShowRevealButton
    {
        get => (bool)GetValue(ShowRevealButtonProperty);
        set => SetValue(ShowRevealButtonProperty, value);
    }

    /// <summary>Moves keyboard focus into the password box itself, not the wrapper.</summary>
    public new bool Focus(FocusState state) => Input.Focus(state);

    private static void OnPasswordPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var field = (PasswordField)sender;
        if (field._syncing)
        {
            return;
        }

        field._syncing = true;
        field.Input.Password = args.NewValue as string ?? string.Empty;
        field._syncing = false;
    }

    private static void OnPlaceholderChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PasswordField)sender).Input.PlaceholderText = args.NewValue as string ?? string.Empty;

    private static void OnHasErrorChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PasswordField)sender).UpdateVisualState();

    private static void OnShowRevealButtonChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var field = (PasswordField)sender;
        var show = args.NewValue is true;

        field.RevealToggle.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        // Without a button in the third column the text would end 8px from the border,
        // so the padding the button was providing moves back onto the PasswordBox.
        field.Input.Margin = show ? new Thickness(13, 0, 4, 0) : new Thickness(13, 0, 12, 0);
    }

    private void OnRevealToggleClick(object sender, RoutedEventArgs args)
    {
        _isRevealed = !_isRevealed;
        ApplyRevealState();

        // Hand focus straight back so typing continues uninterrupted - but only for a
        // pointer click. Doing it unconditionally would trap a keyboard user, who would
        // Tab to this button, press it, and be thrown back into the field every time.
        if (RevealToggle.FocusState == FocusState.Pointer)
        {
            Input.Focus(FocusState.Programmatic);
        }
    }

    private void ApplyRevealState()
    {
        Input.PasswordRevealMode = _isRevealed ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        RevealSlash.Visibility = _isRevealed ? Visibility.Visible : Visibility.Collapsed;

        var label = _isRevealed ? "Hide password" : "Show password";
        AutomationProperties.SetName(RevealToggle, label);
        ToolTipService.SetToolTip(RevealToggle, label);
    }

    private void OnInputPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        Password = Input.Password;
        _syncing = false;
    }

    /// <summary>
    /// Focus is tracked across the whole field, not just the text box, so tabbing onto
    /// the reveal toggle does not drop the focus ring off the border the user is still in.
    /// </summary>
    private void OnFocusChanged(object sender, RoutedEventArgs args)
    {
        _isFocused = Input.FocusState != FocusState.Unfocused
                     || RevealToggle.FocusState != FocusState.Unfocused;

        UpdateVisualState();
    }

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        Submitted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Error beats focus, focus beats hover.</summary>
    private void UpdateVisualState()
    {
        var state = HasError
            ? "Error"
            : _isFocused
                ? "Focused"
                : _isPointerOver
                    ? "PointerOver"
                    : "Normal";

        VisualStateManager.GoToState(this, state, useTransitions: false);
    }
}
