using DoNet.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

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

/// <summary>
/// One labelled field box. See the XAML for why this exists once rather than eighteen
/// times.
/// </summary>
public sealed partial class FieldCell : UserControl
{
    /// <summary>
    /// Guards the two-way sync between <see cref="Value"/> and the input controls, so
    /// writing the property from code does not bounce back through TextChanged and
    /// reset the caret while the user is typing.
    /// </summary>
    private bool _syncing;

    public FieldCell()
    {
        InitializeComponent();
        Refresh();
    }

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnAnyPropertyChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnValueChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(FieldCell),
        new PropertyMetadata(string.Empty, OnAnyPropertyChanged));

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(FieldCellKind), typeof(FieldCell),
        new PropertyMetadata(FieldCellKind.Display, OnAnyPropertyChanged));

    public static readonly DependencyProperty IsSecretProperty = DependencyProperty.Register(
        nameof(IsSecret), typeof(bool), typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    public static readonly DependencyProperty ShowCopyProperty = DependencyProperty.Register(
        nameof(ShowCopy), typeof(bool), typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    public static readonly DependencyProperty ShowGenerateProperty = DependencyProperty.Register(
        nameof(ShowGenerate), typeof(bool), typeof(FieldCell),
        new PropertyMetadata(false, OnAnyPropertyChanged));

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
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

        bool automatic = Kind == FieldCellKind.Automatic;
        bool editable = Kind == FieldCellKind.Editable && !automatic;
        bool hasValue = !string.IsNullOrEmpty(Value);

        Shell.Background = automatic
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardAvatarBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundBrush"];

        if (editable && IsSecret)
        {
            ValueText.Visibility = Visibility.Collapsed;
            ValueInput.Visibility = Visibility.Collapsed;
            SecretInput.Visibility = Visibility.Visible;
            SecretInput.PlaceholderText = Placeholder;

            _syncing = true;
            if (SecretInput.Password != (Value ?? string.Empty))
            {
                SecretInput.Password = Value ?? string.Empty;
            }
            _syncing = false;
        }
        else if (editable)
        {
            ValueText.Visibility = Visibility.Collapsed;
            SecretInput.Visibility = Visibility.Collapsed;
            ValueInput.Visibility = Visibility.Visible;
            ValueInput.PlaceholderText = Placeholder;

            _syncing = true;
            if (ValueInput.Text != (Value ?? string.Empty))
            {
                ValueInput.Text = Value ?? string.Empty;
            }
            _syncing = false;
        }
        else
        {
            ValueInput.Visibility = Visibility.Collapsed;
            SecretInput.Visibility = Visibility.Collapsed;
            ValueText.Visibility = Visibility.Visible;

            // A secret is masked on the preview dialog too, and revealed deliberately.
            // The design does not show a toggle there, but rendering a stored account
            // password in plain text the instant a record is opened is a shoulder
            // surfing problem that the design cannot have intended.
            ValueText.Text = automatic ? Placeholder
                           : !hasValue ? "\u2014"
                           : IsSecret && !_revealed ? new string('\u2022', 10)
                           : Value;

            ValueText.Foreground = automatic
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SearchPlaceholderBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextPrimaryBrush"];
        }

        RevealButton.Visibility = IsSecret && !automatic ? Visibility.Visible : Visibility.Collapsed;
        GenerateButton.Visibility = ShowGenerate && editable ? Visibility.Visible : Visibility.Collapsed;

        // Copy is disabled for empty and automatic values, as the add form states.
        CopyButton.Visibility = ShowCopy && !automatic ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.IsEnabled = hasValue;
        CopyShell.Opacity = hasValue ? 1.0 : 0.45;
    }

    private bool _revealed;

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
    {
        Value = PasswordGenerator.Generate();
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
    private async void StartCopyReset()
    {
        await System.Threading.Tasks.Task.Delay(1200);
        CopyLabel.Text = "COPY";
    }

    private void OnInputChanged(object sender, TextChangedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        Value = ValueInput.Text;
        _syncing = false;
    }

    private void OnSecretChanged(object sender, RoutedEventArgs args)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        Value = SecretInput.Password;
        _syncing = false;
    }
}
