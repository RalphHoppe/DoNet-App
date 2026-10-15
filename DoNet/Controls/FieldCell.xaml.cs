using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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

    /// <summary>
    /// Whole digits only: anything else is stripped as it is typed.
    /// </summary>
    /// <remarks>
    /// A field that means a count or a number should never hold "3m" or "about 5" -
    /// the store would have to parse it back out later, and the parse is what
    /// breaks. The role is enforced by the editor, not by a save-time message the
    /// user reads after the work is done.
    /// </remarks>
    Number,

    /// <summary>
    /// A number that can carry a decimal point and a leading minus.
    /// </summary>
    /// <remarks>
    /// Balances and rates: digits, one separator, one sign. The decimal is
    /// normalised to the invariant dot, so a value typed on a machine set to
    /// comma-separators still parses the way the store reads it.
    /// </remarks>
    Decimal,

    /// <summary>
    /// An IPv4 address, shaped as it is typed.
    /// </summary>
    /// <remarks>
    /// Digits and up to three dots survive the keyboard; each finished group is
    /// held to 255; a value that cannot be an address turns the box's border red
    /// until it can. The role keeps "172.5.42.191" a promise rather than a
    /// suggestion.
    /// </remarks>
    IPv4,
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
        new PropertyMetadata(null, OnAnyPropertyChanged));

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
    /// Identifies the <see cref="AddChoiceLabel"/> property.
    /// </summary>
    public static readonly DependencyProperty AddChoiceLabelProperty = DependencyProperty.Register(
        nameof(AddChoiceLabel),
        typeof(string),
        typeof(FieldCell),
        new PropertyMetadata(null, OnAnyPropertyChanged));

    /// <summary>
    /// The label of the "add" row in the choice drawer, or null for none.
    /// </summary>
    /// <remarks>
    /// Only meaningful with <see cref="FieldInputKind.Choice"/>. This is the escape
    /// hatch for a picker that points at records - the website an account belongs
    /// to - where the thing to pick might not exist yet.
    /// </remarks>
    public string? AddChoiceLabel
    {
        get => (string?)GetValue(AddChoiceLabelProperty);
        set => SetValue(AddChoiceLabelProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="PrefixText"/> property.
    /// </summary>
    public static readonly DependencyProperty PrefixTextProperty = DependencyProperty.Register(
        nameof(PrefixText),
        typeof(string),
        typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnAnyPropertyChanged));

    /// <summary>
    /// Text that sits inside the box before the value, stating what the value means.
    /// </summary>
    /// <remarks>
    /// "1 BTC =" on the exchange rate: the number alone does not say what it prices,
    /// and a label above the box cannot carry an equals sign that reads. Empty on
    /// every field whose meaning its label already carries.
    /// </remarks>
    public string PrefixText
    {
        get => (string)GetValue(PrefixTextProperty);
        set => SetValue(PrefixTextProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="UnitOptions"/> property.
    /// </summary>
    public static readonly DependencyProperty UnitOptionsProperty = DependencyProperty.Register(
        nameof(UnitOptions),
        typeof(IReadOnlyList<string>),
        typeof(FieldCell),
        new PropertyMetadata(null, OnAnyPropertyChanged));

    /// <summary>
    /// The choices for the unit attached to this field's value, or null for no unit.
    /// </summary>
    /// <remarks>
    /// The currency on a balance, the d/w/m/y on a subscription cycle: a unit that
    /// is a choice renders as a picker hung off the value's right edge, inside the
    /// same box, behind a hairline divider - so the unit cannot drift away from the
    /// field it belongs to. Null on every field without one.
    /// </remarks>
    public IReadOnlyList<string>? UnitOptions
    {
        get => (IReadOnlyList<string>?)GetValue(UnitOptionsProperty);
        set => SetValue(UnitOptionsProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="UnitIsText"/> property.
    /// </summary>
    public static readonly DependencyProperty UnitIsTextProperty = DependencyProperty.Register(
        nameof(UnitIsText),
        typeof(bool),
        typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    /// <summary>
    /// Renders the attached unit as a small text input instead of a picker.
    /// </summary>
    /// <remarks>
    /// For a unit that is typed rather than chosen: the number on a port, whose
    /// protocol is the value and whose number rides beside it. Ignored when
    /// <see cref="UnitOptions"/> is set - a unit with choices is always a picker.
    /// </remarks>
    public bool UnitIsText
    {
        get => (bool)GetValue(UnitIsTextProperty);
        set => SetValue(UnitIsTextProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="UnitIsNumeric"/> property.
    /// </summary>
    public static readonly DependencyProperty UnitIsNumericProperty = DependencyProperty.Register(
        nameof(UnitIsNumeric),
        typeof(bool),
        typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    /// <summary>
    /// Whether a typed unit takes digits only - the number on a port.
    /// </summary>
    /// <remarks>
    /// The role is the same one the value editors enforce; it belongs to the unit
    /// too, or the port would guard its protocol and store "55a475" beside it.
    /// </remarks>
    public bool UnitIsNumeric
    {
        get => (bool)GetValue(UnitIsNumericProperty);
        set => SetValue(UnitIsNumericProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="AllowChoiceCustom"/> property.
    /// </summary>
    public static readonly DependencyProperty AllowChoiceCustomProperty = DependencyProperty.Register(
        nameof(AllowChoiceCustom),
        typeof(bool),
        typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    /// <summary>
    /// Whether the choice drawer takes an answer its list does not have.
    /// </summary>
    /// <remarks>
    /// The country field has always allowed one - the world's list is the
    /// world's business. A brand list needs the same door, because wallet
    /// brands arrive faster than any list follows; a typed answer commits as
    /// itself. Only meaningful with <see cref="FieldInputKind.Choice"/>.
    /// </remarks>
    public bool AllowChoiceCustom
    {
        get => (bool)GetValue(AllowChoiceCustomProperty);
        set => SetValue(AllowChoiceCustomProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="UnitValue"/> property.
    /// </summary>
    public static readonly DependencyProperty UnitValueProperty = DependencyProperty.Register(
        nameof(UnitValue),
        typeof(string),
        typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnAnyPropertyChanged));

    /// <summary>
    /// The attached unit's value: the chosen row's label, or the typed text.
    /// </summary>
    /// <remarks>
    /// Bind this two ways, as with <see cref="Value"/>. Read-only modes show it as
    /// plain text after the value, still behind the divider, so the preview says
    /// "13.7 | USD" rather than a bare number with its currency somewhere else.
    /// </remarks>
    public string UnitValue
    {
        get => (string)GetValue(UnitValueProperty);
        set => SetValue(UnitValueProperty, value);
    }

    /// <summary>
    /// Identifies the <see cref="UnitPlaceholder"/> property.
    /// </summary>
    public static readonly DependencyProperty UnitPlaceholderProperty = DependencyProperty.Register(
        nameof(UnitPlaceholder),
        typeof(string),
        typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnAnyPropertyChanged));

    /// <summary>The unit's placeholder - "Currency" on the balance, the number's
    /// example on the port.</summary>
    public string UnitPlaceholder
    {
        get => (string)GetValue(UnitPlaceholderProperty);
        set => SetValue(UnitPlaceholderProperty, value);
    }

    /// <summary>Raised when the drawer's "add" row is picked. Carries nothing.</summary>
    public event EventHandler? AddChoiceRequested;

    /// <summary>
    /// Raised whenever the user changed the value and the change has landed in
    /// <see cref="Value"/>.
    /// </summary>
    /// <remarks>
    /// The XAML dialogs bind to <see cref="Value"/> two ways and need nothing else.
    /// The service record form builds its cells in code - there is no x:Bind to
    /// carry the value out - so this is the channel: every editor funnels here,
    /// carrying the new value.
    /// </remarks>
    public event EventHandler<string>? ValueCommitted;

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
               || ReferenceEquals(element, DateInput)
               || ReferenceEquals(element, UnitInput)
               || ReferenceEquals(element, UnitChoice));

    /// <summary>
    /// The attached unit's focus target, or null when this cell has no unit on
    /// screen.
    /// </summary>
    /// <remarks>
    /// The Tab driver hops value to unit to next field, so a merged field is two
    /// stops without the driver having to know which kind of unit it is.
    /// </remarks>
    public Control? UnitFocusTarget()
        => UnitInput.Visibility == Visibility.Visible ? UnitInput
         : UnitChoice.Visibility == Visibility.Visible ? UnitChoice
         : null;

    /// <summary>
    /// Whether <paramref name="element"/> is this cell's unit editor rather than
    /// its value editor.
    /// </summary>
    public bool UnitOwnsFocus(object? element)
        => element is not null
           && (ReferenceEquals(element, UnitInput)
               || ReferenceEquals(element, UnitChoice));

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
                    ShowChoice(value, Catalogs.Genders, allowCustom: false, flags: false);
                    break;
                case FieldInputKind.Country:
                    ShowChoice(value, Catalogs.Countries, allowCustom: true, flags: true);
                    break;

                case FieldInputKind.Choice:
                    ShowChoice(
                        value,
                        Options ?? Array.Empty<string>(),
                        allowCustom: AllowChoiceCustom,
                        flags: false);
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

        ShowPrefix();
        ShowUnit(editable);

        // A value set from outside - a record read into the form - gets the same
        // verdict as one typed: a stored address that cannot be an address says
        // so the moment it appears.
        UpdateValidity(editable ? value : string.Empty);

        ApplyTabOrder();
    }

    /// <summary>States what the value means, inside the box before it.</summary>
    private void ShowPrefix()
    {
        PrefixLabel.Text = PrefixText;
        PrefixLabel.Visibility = string.IsNullOrEmpty(PrefixText)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    /// <summary>
    /// Dresses the attached unit zone for the current mode, or hides it outright.
    /// </summary>
    /// <remarks>
    /// Editable fields with a unit get the picker or the small input; read-only
    /// fields get the unit as text behind the same divider, so the preview keeps
    /// the pairing the form promised. No unit, no zone - the divider is the zone's
    /// to hide, so a field without one loses nothing.
    /// </remarks>
    private void ShowUnit(bool editable)
    {
        // A phone field carries its own drawer: the calling code, with flags,
        // drawn from the catalog rather than handed in. The host binds nothing
        // but the unit's value - the list is the field's to know.
        IReadOnlyList<string>? options = UnitOptions
            ?? (InputKind == FieldInputKind.Phone ? Catalogs.CallingCodes : null);

        string unit = UnitValue ?? string.Empty;
        bool picker = editable && options is not null;
        bool typed = editable && options is null && UnitIsText;
        bool readOnly = !editable && !string.IsNullOrEmpty(unit);

        UnitDivider.Visibility = picker || typed || readOnly
            ? Visibility.Visible
            : Visibility.Collapsed;

        UnitChoice.Visibility = picker ? Visibility.Visible : Visibility.Collapsed;
        UnitInput.Visibility = typed ? Visibility.Visible : Visibility.Collapsed;
        UnitText.Visibility = readOnly ? Visibility.Visible : Visibility.Collapsed;

        if (picker)
        {
            UnitChoice.Placeholder = UnitPlaceholder;
            UnitChoice.AllowCustom = false;
            UnitChoice.ShowFlags = false;
            UnitChoice.AddLabel = null;
            UnitChoice.ItemIconKind = null;

            if (!ReferenceEquals(UnitChoice.Options, options))
            {
                UnitChoice.Options = options;
            }

            _syncing = true;
            UnitChoice.Value = unit;
            _syncing = false;
        }
        else if (typed)
        {
            UnitInput.PlaceholderText = UnitPlaceholder;

            _syncing = true;
            if (UnitInput.Text != unit)
            {
                UnitInput.Text = unit;
            }
            _syncing = false;
        }

        UnitText.Text = unit;
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
        string value, IReadOnlyList<string> options, bool allowCustom, bool flags)
    {
        ChoiceInput.Visibility = Visibility.Visible;
        ChoiceInput.Placeholder = Placeholder;
        ChoiceInput.AllowCustom = allowCustom;
        ChoiceInput.ShowFlags = flags;
        ChoiceInput.AddLabel = AddChoiceLabel;

        // Each row carries the field's own icon, which is the rule the rest of the
        // app already follows: one mark per field, the same on the card and in the
        // dialog. Countries are the exception, because a flag says more.
        ChoiceInput.ItemIconKind = flags ? null : IconKind;

        // Reassign whenever the list itself changed. The original guard set the
        // source once, which is right for the static catalogs and wrong for a list
        // that is data: a website added after this cell was first shown would never
        // appear in the picker.
        if (!ReferenceEquals(ChoiceInput.Options, options))
        {
            ChoiceInput.Options = options;
        }

        // A stored value that is not in the list still shows, as its own text. The
        // picker treats the value as display text and only writes one back when a
        // row is actually chosen, so nothing is quietly rewritten on the way in.
        _syncing = true;
        ChoiceInput.Value = value;
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

        // The unit is one stop after the value it belongs to. A picker unit shares
        // the value's stop - it is one motion, value then unit - while a typed unit
        // (the port's number) is a real second field and takes the next number, so
        // the cell after it has to count past it.
        UnitChoice.TabIndex = TabOrder;
        UnitInput.TabIndex = TabOrder + 1;
    }

    private static Microsoft.UI.Xaml.Media.Brush Brush(string key)
        => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];

    private void Commit(string value)
    {
        _syncing = true;
        Value = value;
        _syncing = false;

        ValueCommitted?.Invoke(this, value);
    }

    private void OnRevealClick(object sender, RoutedEventArgs args)
    {
        // The press takes the reveal over from the generate's timed one, whichever
        // way it was going.
        _revealGeneration++;
        SetRevealed(!_revealed);
    }

    /// <summary>Applies a reveal state in one place: mask, icon, and value.</summary>
    private void SetRevealed(bool revealed)
    {
        _revealed = revealed;
        SecretInput.PasswordRevealMode = revealed
            ? PasswordRevealMode.Visible
            : PasswordRevealMode.Hidden;
        EyeSlash.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void OnGenerateClick(object sender, RoutedEventArgs args)
    {
        Commit(PasswordGenerator.Generate());

        // A generated password lands masked, and a masked change reads as nothing
        // having happened - the only other sign is the button's own press. So the
        // generation shows its face: the new password is revealed for a moment,
        // then masks itself again. A user who is already watching the field keeps
        // nothing waiting; one who looks away can still press the eye.
        if (IsSecret && !_revealed)
        {
            SetRevealed(true);
            _ = MaskAgainAfterGenerateAsync();
        }
    }

    /// <summary>How long a generated password stays revealed before masking itself.</summary>
    private const int GeneratedRevealMs = 2500;

    private int _revealGeneration;

    /// <summary>Masks the password again unless something took the reveal over.</summary>
    private async System.Threading.Tasks.Task MaskAgainAfterGenerateAsync()
    {
        int generation = unchecked(++_revealGeneration);

        try
        {
            await System.Threading.Tasks.Task.Delay(GeneratedRevealMs);

            if (generation == _revealGeneration && _revealed)
            {
                SetRevealed(false);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Masking the generated password again failed", error);
        }
    }

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
        if (_syncing)
        {
            return;
        }

        string text = ValueInput.Text;
        string cleaned = CleanForRole(InputKind, text);

        if (cleaned != text)
        {
            // The editor, not a message, is what enforces the role: the character
            // that cannot belong never appears. The rewrite raises this handler
            // again, and the second pass - clean text - is the one that commits.
            _syncing = true;
            int caret = Math.Clamp(
                ValueInput.SelectionStart - (text.Length - cleaned.Length),
                0, cleaned.Length);
            ValueInput.Text = cleaned;
            ValueInput.SelectionStart = caret;
            _syncing = false;
            return;
        }

        UpdateValidity(text);
        Commit(text);
    }

    /// <summary>
    /// What may remain of <paramref name="text"/> under this input kind's role.
    /// </summary>
    private static string CleanForRole(FieldInputKind kind, string text)
    {
        switch (kind)
        {
            case FieldInputKind.Number:
            {
                // Digits only; a role that means a count does not negotiate.
                char[] keep = text.Where(char.IsDigit).ToArray();
                return keep.Length == 0 ? string.Empty : new string(keep);
            }

            case FieldInputKind.Decimal:
            {
                // Digits, the first dot, and one leading minus. Commas are
                // stripped rather than treated as separators: a pasted
                // "1,234.5" is the same number as "1234.5" to the store.
                bool minus = text.StartsWith('-');
                string body = minus ? text[1..] : text;
                int dot = body.IndexOf('.');
                string keep = new(body.Where(char.IsDigit).ToArray());
                if (dot >= 0)
                {
                    keep = dot <= keep.Length
                        ? keep.Insert(dot, ".")
                        : keep + ".";
                }
                return keep.Length == 0 ? string.Empty : (minus ? "-" + keep : keep);
            }

            case FieldInputKind.IPv4:
            {
                // Digits and up to three dots: four groups is the whole address.
                StringBuilder shaped = new();
                int dots = 0;
                foreach (char c in text)
                {
                    if (char.IsDigit(c))
                    {
                        shaped.Append(c);
                    }
                    else if (c == '.' && dots < 3
                             && (shaped.Length == 0 || shaped[^1] != '.'))
                    {
                        shaped.Append('.');
                        dots++;
                    }
                }

                return shaped.ToString();
            }

            case FieldInputKind.Phone:
            {
                // Digits with the spaces, dashes and brackets people type, and a
                // plus - which the calling-code drawer owns, but an imported
                // value may still carry one.
                bool plus = text.StartsWith('+');
                string body = plus ? text[1..] : text;
                string keep = new(body.Where(c =>
                    char.IsDigit(c) || c is ' ' or '-' or '(' or ')').ToArray());
                return keep.Length == 0 ? string.Empty : (plus ? "+" + keep : keep);
            }

            default:
                return text;
        }
    }

    /// <summary>True when a non-empty value cannot be what its kind promises.</summary>
    private static bool IsInvalid(FieldInputKind kind, string text)
        => kind == FieldInputKind.IPv4 && text.Length > 0 && !IsValidIPv4(text);

    /// <summary>
    /// Whether the text is, or is on its way to being, a valid IPv4 address.
    /// </summary>
    /// <remarks>
    /// Tolerant of a half-typed address - "172.5." is fine while the owner is
    /// still going - but every finished group must be 0 to 255 and there can be
    /// no fifth group.
    /// </remarks>
    private static bool IsValidIPv4(string text)
    {
        if (text.StartsWith('.') || text.EndsWith(".."))
        {
            return false;
        }

        string[] groups = text.Split('.');
        if (groups.Length > 4)
        {
            return false;
        }

        foreach (string group in groups)
        {
            if (group.Length == 0)
            {
                // An empty last group is the trailing dot of an address still
                // being typed; anything else cannot happen - the cleaner keeps
                // doubled dots out.
                return ReferenceEquals(group, groups[^1]);
            }

            // "01" is not an octet anyone types on purpose; leading zeros are
            // how addresses stop being what they look like.
            if (group.Length > 3 || (group.Length > 1 && group[0] == '0'))
            {
                return false;
            }

            if (int.Parse(group, CultureInfo.InvariantCulture) > 255)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Turns the box's border red while the value cannot be what it claims, and
    /// back the moment it can.
    /// </summary>
    private void UpdateValidity(string text)
    {
        bool invalid = IsInvalid(InputKind, text);
        Shell.BorderBrush = Brush(invalid ? "DangerBrush" : "SearchBorderBrush");
        ToolTipService.SetToolTip(
            Shell,
            invalid ? "Enter a valid IPv4 address, like 172.5.42.191" : null);
    }

    private void OnSecretChanged(object sender, RoutedEventArgs args)
    {
        if (!_syncing)
        {
            Commit(SecretInput.Password);
        }
    }

    private void OnUnitChoiceChosen(object? sender, string picked)
    {
        if (!_syncing)
        {
            UnitValue = picked;
        }
    }

    private void OnUnitInputChanged(object sender, TextChangedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }

        string text = UnitInput.Text;
        if (UnitIsNumeric)
        {
            string cleaned = new(text.Where(char.IsDigit).ToArray());
            if (cleaned != text)
            {
                _syncing = true;
                UnitInput.Text = cleaned;
                _syncing = false;
                return;             // the rewrite raises this handler again
            }
        }

        UnitValue = text;
    }

    private void OnChoiceChanged(object? sender, string picked)
    {
        if (!_syncing)
        {
            Commit(picked);
        }
    }

    private void OnChoiceAddRequested(object? sender, EventArgs args)
        => AddChoiceRequested?.Invoke(this, EventArgs.Empty);

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
