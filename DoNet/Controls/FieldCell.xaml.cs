using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DoNet.Models;
using DoNet.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml.Input;

namespace DoNet.Controls;

/// <summary>How a <see cref="FieldCell"/> presents its value.</summary>
public enum FieldCellKind
{
    /// <summary>Read-only text, as on the preview dialog.</summary>
    Display,

    /// <summary>An input, as on the add and edit forms.</summary>
    Editable,

    /// <summary>Greyed and inert: the store assigns this one on save.</summary>
    Automatic,
}

/// <summary>Which editor a <see cref="FieldCell"/> uses when it is editable.</summary>
public enum FieldInputKind
{
    Text,
    Email,
    Phone,
    PostalCode,

    /// <summary>Wraps and accepts newlines. The note.</summary>
    Multiline,

    /// <summary>A calendar. Date of birth.</summary>
    Date,

    /// <summary>A fixed list.</summary>
    Gender,

    /// <summary>A list that can still be typed into.</summary>
    Country,

    /// <summary>
    /// A fixed list the host supplies through <see cref="FieldCell.Options"/>.
    /// </summary>
    /// <remarks>
    /// Gender and Country read static catalogs. This one is for a list that is data -
    /// the saved websites an account can belong to - which is not known until the
    /// store has been read and changes while the app is running.
    /// </remarks>
    Choice,
}

/// <summary>
/// One labelled field box. See the XAML for why this exists once rather than eighteen
/// times.
/// </summary>
public sealed partial class FieldCell : UserControl
{
    /// <summary>The format the design asks for: "DD / MM / YYYY".</summary>
    private const string DateFormat = "dd / MM / yyyy";

    /// <summary>
    /// Guards the two-way sync between <see cref="Value"/> and the editors, so writing
    /// the property from code does not bounce back through a change event and reset the
    /// caret while the user is typing.
    /// </summary>
    /// <summary>How long the COPY button reads "COPIED" after a press.</summary>
    private const int CopyFeedbackMs = 1200;

    private bool _syncing;
    private int _copyGeneration;

    private bool _revealed;

    public FieldCell()
    {
        InitializeComponent();
        Refresh();
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnAnyPropertyChanged));

    public static readonly DependencyProperty IconKindProperty = DependencyProperty.Register(
        nameof(IconKind), typeof(string), typeof(FieldCell),
        new PropertyMetadata(null, OnAnyPropertyChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnValueChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnAnyPropertyChanged));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(FieldCellKind), typeof(FieldCell),
        new PropertyMetadata(FieldCellKind.Display, OnAnyPropertyChanged));

    public static readonly DependencyProperty InputKindProperty = DependencyProperty.Register(
        nameof(InputKind), typeof(FieldInputKind), typeof(FieldCell),
        new PropertyMetadata(FieldInputKind.Text, OnAnyPropertyChanged));

    public static readonly DependencyProperty IsSecretProperty = DependencyProperty.Register(
        nameof(IsSecret), typeof(bool), typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    public static readonly DependencyProperty ShowCopyProperty = DependencyProperty.Register(
        nameof(ShowCopy), typeof(bool), typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    public static readonly DependencyProperty ShowGenerateProperty = DependencyProperty.Register(
        nameof(ShowGenerate), typeof(bool), typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    /// <summary>The list offered when <see cref="InputKind"/> is Choice.</summary>
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options),
        typeof(IReadOnlyList<string>),
        typeof(FieldCell),
        new PropertyMetadata(null, OnAnyChanged));

    public static readonly DependencyProperty TabOrderProperty = DependencyProperty.Register(
        nameof(TabOrder), typeof(int), typeof(FieldCell),
        new PropertyMetadata(0, OnAnyPropertyChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Which icon sits beside the caption, named as in <see cref="LineIconData.Names"/>.
    /// </summary>
    public string? IconKind
    {
        get => (string?)GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public FieldCellKind Kind
    {
        get => (FieldCellKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public FieldInputKind InputKind
    {
        get => (FieldInputKind)GetValue(InputKindProperty);
        set => SetValue(InputKindProperty, value);
    }

    /// <summary>Masks the value and offers a reveal toggle.</summary>
    public bool IsSecret
    {
        get => (bool)GetValue(IsSecretProperty);
        set => SetValue(IsSecretProperty, value);
    }

    public bool ShowCopy
    {
        get => (bool)GetValue(ShowCopyProperty);
        set => SetValue(ShowCopyProperty, value);
    }

    public bool ShowGenerate
    {
        get => (bool)GetValue(ShowGenerateProperty);
        set => SetValue(ShowGenerateProperty, value);
    }

    /// <summary>
    /// Where this cell sits in the Tab sequence.
    /// </summary>
    /// <remarks>
    /// Set on the inner editor rather than on the cell, because the editor is what
    /// takes focus. Without it Tab follows the visual tree, which for a three-column
    /// grid built from three stacked panels means going down the whole first column
    /// before reaching the top of the second.
    /// </remarks>
    public int TabOrder
    {
        get => (int)GetValue(TabOrderProperty);
        set => SetValue(TabOrderProperty, value);
    }

    /// <summary>
    /// The list offered when <see cref="InputKind"/> is
    /// <see cref="FieldInputKind.Choice"/>. Ignored for every other kind.
    /// </summary>
    public IReadOnlyList<string>? Options
    {
        get => (IReadOnlyList<string>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>
    /// The input this cell is currently showing, or null if it is showing none -
    /// a display cell, or an automatic one like the record number.
    /// </summary>
    /// <remarks>
    /// One cell hosts four different inputs and shows whichever the field kind calls
    /// for, so "the input" is a question that can only be answered at runtime.
    /// </remarks>
    private Control? ActiveInput()
    {
        if (ValueInput.Visibility == Visibility.Visible)
        {
            return ValueInput;
        }

        if (SecretInput.Visibility == Visibility.Visible)
        {
            return SecretInput;
        }

        if (ChoiceInput.Visibility == Visibility.Visible)
        {
            return ChoiceInput;
        }

        return DateInput.Visibility == Visibility.Visible ? DateInput : null;
    }

    /// <summary>Moves keyboard focus into this cell. False if it has nothing to focus.</summary>
    public bool TryFocus()
    {
        Control? target = ActiveInput();

        return target is not null
            && target.IsEnabled
            && target.Focus(FocusState.Keyboard);
    }

    /// <summary>Whether <paramref name="element"/> is this cell's input.</summary>
    public bool OwnsFocus(object? element)
        => element is not null
           && (ReferenceEquals(element, ValueInput)
               || ReferenceEquals(element, SecretInput)
               || ReferenceEquals(element, ChoiceInput)
               || ReferenceEquals(element, DateInput));

    private static void OnAnyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((FieldCell)d).Refresh();

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        FieldCell cell = (FieldCell)d;
        if (!cell._syncing)
        {
            cell.Refresh();
        }
    }

    /// <summary>
    /// Applies every property at once. One method rather than a handler per property:
    /// the variants interact (an automatic field is never editable, a secret is never
    /// shown as plain text), and separate handlers would have to re-derive that.
    /// </summary>
    private void Refresh()
    {
        if (LabelText is null)
        {
            return;
        }

        LabelText.Text = Label;
        LabelText.IconKind = IconKind;

        bool automatic = Kind == FieldCellKind.Automatic;
        bool editable = Kind == FieldCellKind.Editable && !automatic;
        bool hasValue = !string.IsNullOrEmpty(Value);
        string value = Value ?? string.Empty;

        Shell.Background = Brush(automatic ? "CardAvatarBrush" : "CardBackgroundBrush");

        ValueText.Visibility = Visibility.Collapsed;
        ValueInput.Visibility = Visibility.Collapsed;
        SecretInput.Visibility = Visibility.Collapsed;
        ChoiceInput.Visibility = Visibility.Collapsed;
        DateInput.Visibility = Visibility.Collapsed;

        if (!editable)
        {
            ShowReadOnly(automatic, hasValue, value);
        }
        else if (IsSecret)
        {
            ShowSecret(value);
        }
        else
        {
            switch (InputKind)
            {
                case FieldInputKind.Gender:
                    ShowChoice(value, Catalogs.Genders, editableText: false);
                    break;
                case FieldInputKind.Country:
                    ShowChoice(value, Catalogs.Countries, editableText: true);
                    break;

                case FieldInputKind.Choice:
                    ShowChoice(value, Options ?? Array.Empty<string>(), editableText: false);
                    break;
                case FieldInputKind.Date:
                    ShowDate(value);
                    break;
                default:
                    ShowText(value);
                    break;
            }
        }

        RevealButton.Visibility = IsSecret && !automatic ? Visibility.Visible : Visibility.Collapsed;
        GenerateButton.Visibility = ShowGenerate && editable ? Visibility.Visible : Visibility.Collapsed;

        // Copy is disabled for empty and automatic values, as the add form states.
        CopyButton.Visibility = ShowCopy && !automatic ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = hasValue;
        CopyShell.Opacity = hasValue ? 1.0 : 0.45;

        ApplyTabOrder();
    }

    private void ShowReadOnly(bool automatic, bool hasValue, string value)
    {
        ValueText.Visibility = Visibility.Visible;

        // A secret is masked on the preview dialog too, and revealed deliberately.
        // The design does not show a toggle there, but rendering a stored account
        // password in plain text the instant a record is opened is a shoulder
        // surfing problem that the design cannot have intended.
        ValueText.Text = automatic ? Placeholder
                       : !hasValue ? "\u2014"
                       : IsSecret && !_revealed ? new string('\u2022', 10)
                       : value;

        ValueText.Foreground = Brush(automatic ? "SearchPlaceholderBrush" : "TextPrimaryBrush");
        ValueText.TextWrapping = InputKind == FieldInputKind.Multiline
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;
    }

    private void ShowSecret(string value)
    {
        SecretInput.Visibility = Visibility.Visible;
        SecretInput.PlaceholderText = Placeholder;

        _syncing = true;
        if (SecretInput.Password != value)
        {
            SecretInput.Password = value;
        }
        _syncing = false;
    }

    private void ShowText(string value)
    {
        ValueInput.Visibility = Visibility.Visible;
        ValueInput.PlaceholderText = Placeholder;

        bool multiline = InputKind == FieldInputKind.Multiline;
        ValueInput.AcceptsReturn = multiline;
        ValueInput.TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap;
        ValueInput.MinHeight = multiline ? 44 : 0;

        // A note is read from its first line down; a one-line field is centred in its
        // row. Same control, so the alignment has to follow the kind.
        ValueInput.VerticalContentAlignment =
            multiline ? VerticalAlignment.Top : VerticalAlignment.Center;

        ValueInput.InputScope = ScopeFor(InputKind);

        _syncing = true;
        if (ValueInput.Text != value)
        {
            ValueInput.Text = value;
        }
        _syncing = false;
    }

    private void ShowChoice(
        string value, IReadOnlyList<string> options, bool editableText)
    {
        ChoiceInput.Visibility = Visibility.Visible;
        ChoiceInput.PlaceholderText = Placeholder;
        ChoiceInput.IsEditable = editableText;

        // Reassign whenever the list itself changed. The original guard set the
        // source once, which is right for the static catalogs and wrong for a list
        // that is data: a website added after this cell was first shown would never
        // appear in the picker.
        if (!ReferenceEquals(ChoiceInput.ItemsSource, options))
        {
            ChoiceInput.ItemsSource = options;
        }

        _syncing = true;

        // A stored value that is not in the list still has to show. For the editable
        // country box that is just its text; for the fixed gender list, selecting
        // nothing leaves the placeholder visible rather than silently rewriting the
        // record to something it never said.
        string? match = options.FirstOrDefault(
            o => string.Equals(o, value, StringComparison.OrdinalIgnoreCase));

        ChoiceInput.SelectedItem = match;

        if (editableText && ChoiceInput.Text != value)
        {
            ChoiceInput.Text = value;
        }

        _syncing = false;
    }

    private void ShowDate(string value)
    {
        DateInput.Visibility = Visibility.Visible;
        DateInput.PlaceholderText = string.IsNullOrEmpty(Placeholder) ? "DD / MM / YYYY" : Placeholder;

        _syncing = true;
        DateInput.Date = TryParseDate(value, out DateTimeOffset parsed) ? parsed : null;
        _syncing = false;
    }

    /// <summary>
    /// Parses what the store holds.
    /// </summary>
    /// <remarks>
    /// Invariant culture with an explicit format, because the stored string must mean
    /// the same thing on every machine. Parsing with the current culture would read
    /// 03/04/2001 as March on one install and April on another, silently changing
    /// people's birthdays when the record moves.
    /// </remarks>
    private static bool TryParseDate(string value, out DateTimeOffset result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
                   value, DateFormat, CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out result)
               || DateTimeOffset.TryParse(
                   value, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    }

    private static InputScope ScopeFor(FieldInputKind kind)
    {
        InputScopeNameValue name = kind switch
        {
            FieldInputKind.Email => InputScopeNameValue.EmailSmtpAddress,
            FieldInputKind.Phone => InputScopeNameValue.TelephoneNumber,
            FieldInputKind.PostalCode => InputScopeNameValue.AlphanumericFullWidth,
            _ => InputScopeNameValue.Default,
        };

        InputScope scope = new();
        scope.Names.Add(new InputScopeName(name));
        return scope;
    }

    private void ApplyTabOrder()
    {
        if (TabOrder <= 0)
        {
            return;
        }

        ValueInput.TabIndex = TabOrder;
        SecretInput.TabIndex = TabOrder;
        ChoiceInput.TabIndex = TabOrder;
        DateInput.TabIndex = TabOrder;
    }

    private static Microsoft.UI.Xaml.Media.Brush Brush(string key)
        => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];

    private void Commit(string value)
    {
        _syncing = true;
        Value = value;
        _syncing = false;
    }

    private void OnRevealClick(object sender, RoutedEventArgs args)
    {
        _revealed = !_revealed;
        SecretInput.PasswordRevealMode = _revealed
            ? PasswordRevealMode.Visible
            : PasswordRevealMode.Hidden;
        EyeSlash.Visibility = _revealed ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void OnGenerateClick(object sender, RoutedEventArgs args)
        => Value = PasswordGenerator.Generate();

    private void OnCopyClick(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrEmpty(Value))
        {
            return;
        }

        DataPackage package = new();
        package.SetText(Value);
        Clipboard.SetContent(package);

        CopyLabel.Text = "COPIED";
        StartCopyReset();
    }

    /// <summary>Returns the COPY label to its resting text shortly after a copy.</summary>
    /// <remarks>
    /// Generation-counted. Two copies in quick succession used to leave two timers
    /// running, and the first to finish reset the label while the second copy was still
    /// meant to be showing - so the confirmation vanished about a second early. Only the
    /// most recent press owns the label.
    ///
    /// Wrapped because an async void handler that throws takes the process with it.
    /// </remarks>
    private async void StartCopyReset()
    {
        int generation = unchecked(++_copyGeneration);

        try
        {
            await System.Threading.Tasks.Task.Delay(CopyFeedbackMs);

            if (generation == _copyGeneration)
            {
                CopyLabel.Text = "COPY";
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Resetting the copy label failed", error);
        }
    }

    private void OnInputChanged(object sender, TextChangedEventArgs args)
    {
        if (!_syncing)
        {
            Commit(ValueInput.Text);
        }
    }

    private void OnSecretChanged(object sender, RoutedEventArgs args)
    {
        if (!_syncing)
        {
            Commit(SecretInput.Password);
        }
    }

    private void OnChoiceChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_syncing && ChoiceInput.SelectedItem is string picked)
        {
            Commit(picked);
        }
    }

    /// <summary>A country typed by hand rather than picked from the list.</summary>
    private void OnChoiceTextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        if (!_syncing)
        {
            Commit(args.Text);

            // Tells the ComboBox we handled it, so it does not try to add the text to
            // the items source.
            args.Handled = true;
        }
    }

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }

        Commit(args.NewDate is { } date
            ? date.ToString(DateFormat, CultureInfo.InvariantCulture)
            : string.Empty);
    }
}
