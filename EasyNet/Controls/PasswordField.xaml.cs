using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace EasyNet.Controls;

/// <summary>
/// The design's password field: a padlock, the word, and a keyhole that
/// shows it. See the XAML for how it is drawn.
/// </summary>
/// <remarks>
/// The word is never bound: the box below it keeps its value out of the
/// binding system on purpose, so this control hands it out through events
/// and a property instead, and the pages pass it along to their view models
/// the moment it changes.
///</remarks>
public sealed partial class PasswordField : UserControl
{
    /// <summary>Identifies the <see cref="Label"/> property.</summary>
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(PasswordField),
        new PropertyMetadata(string.Empty));

    /// <summary>Identifies the <see cref="Placeholder"/> property.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(PasswordField),
        new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public PasswordField()
    {
        InitializeComponent();
    }

    /// <summary>The line above the pill, e.g. "Password".</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>The word shown when nothing has been typed, e.g. "Enter your password".</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>The word as typed, or the empty string.</summary>
    public string Password => Input.Password;

    /// <summary>The word changed. The page carries it to its view model.</summary>
    public event EventHandler? PasswordChanged;

    /// <summary>Enter was pressed in the field. The page decides what that submits.</summary>
    public event EventHandler? Submitted;

    /// <summary>
    /// Turns the field's edges to ink, or back. The word's own judgement
    /// lives in the page; this only says how it looks.
    /// </summary>
    public void SetValidity(bool valid) =>
        _ = VisualStateManager.GoToState(this, valid ? "Valid" : "Invalid", useTransitions: true);

    /// <summary>Puts the caret in the word.</summary>
    public void FocusInput() => Input.Focus(FocusState.Programmatic);

    /// <summary>Empties the word.</summary>
    public void Clear() => Input.Password = string.Empty;

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((PasswordField)d).Input.PlaceholderText = (string)e.NewValue;

    private void OnInputPasswordChanged(object sender, RoutedEventArgs args) =>
        PasswordChanged?.Invoke(this, EventArgs.Empty);

    private void OnInputFocusChanged(object sender, RoutedEventArgs args) =>
        _ = VisualStateManager.GoToState(
            this,
            Input.FocusState == FocusState.Unfocused ? "Resting" : "Focused",
            useTransitions: true);

    private void OnInputKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Enter)
        {
            args.Handled = true;
            Submitted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRevealToggleClick(object sender, RoutedEventArgs args)
    {
        bool shown = Input.PasswordRevealMode == PasswordRevealMode.Hidden;
        Input.PasswordRevealMode = shown ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;

        // The keyhole answers through the states, so the palette stays in XAML.
        _ = VisualStateManager.GoToState(this, shown ? "Shown" : "Hid", useTransitions: true);
    }
}
