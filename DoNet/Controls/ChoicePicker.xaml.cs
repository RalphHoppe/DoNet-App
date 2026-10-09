using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.System;

namespace DoNet.Controls;

/// <summary>
/// The app's dropdown: a value row that opens a white, rounded drawer of options.
/// </summary>
/// <remarks>
/// <para>
/// Written rather than restyled. A ComboBox's list is a system surface - its own
/// background, its own 8px corners, system type and the system accent for selection -
/// reached through a dozen theme keys and a template the field cannot see. Owning the
/// list is less code than overriding all of that, and it is the only way to put a
/// flag on a row.
/// </para>
/// <para>
/// The drawer is a Flyout, not a Popup. Light dismiss, keeping itself inside the
/// window, flipping above the field when there is no room below and placing itself
/// correctly at every scale are all things a Flyout already does, and all things that
/// have to be right for the app to work at any window size.
/// </para>
/// </remarks>
public sealed partial class ChoicePicker : UserControl
{
    /// <summary>Lists at least this long get a filter box.</summary>
    private const int FilterThreshold = 12;

    private readonly ObservableCollection<ChoiceOption> _rows = new();
    private readonly Storyboard _spin = new();
    private readonly DoubleAnimation _spinAngle = new()
    {
        Duration = new Duration(TimeSpan.FromSeconds(0.18)),
        EnableDependentAnimation = true,
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
    };

    private bool _syncing;

    /// <summary>Identifies the <see cref="Options"/> property.</summary>
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(IReadOnlyList<string>), typeof(ChoicePicker),
        new PropertyMetadata(null, OnSurfaceChanged));

    /// <summary>Identifies the <see cref="Value"/> property.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ChoicePicker),
        new PropertyMetadata(string.Empty, OnSurfaceChanged));

    /// <summary>Identifies the <see cref="Placeholder"/> property.</summary>
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(ChoicePicker),
        new PropertyMetadata(string.Empty, OnSurfaceChanged));

    /// <summary>Identifies the <see cref="ItemIconKind"/> property.</summary>
    public static readonly DependencyProperty ItemIconKindProperty = DependencyProperty.Register(
        nameof(ItemIconKind), typeof(string), typeof(ChoicePicker),
        new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="ShowFlags"/> property.</summary>
    public static readonly DependencyProperty ShowFlagsProperty = DependencyProperty.Register(
        nameof(ShowFlags), typeof(bool), typeof(ChoicePicker),
        new PropertyMetadata(false, OnSurfaceChanged));

    /// <summary>Identifies the <see cref="AllowCustom"/> property.</summary>
    public static readonly DependencyProperty AllowCustomProperty = DependencyProperty.Register(
        nameof(AllowCustom), typeof(bool), typeof(ChoicePicker),
        new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="AddLabel"/> property.</summary>
    public static readonly DependencyProperty AddLabelProperty = DependencyProperty.Register(
        nameof(AddLabel), typeof(string), typeof(ChoicePicker),
        new PropertyMetadata(null));

    /// <summary>Creates the control.</summary>
    public ChoicePicker()
    {
        InitializeComponent();

        Rows.ItemsSource = _rows;

        Storyboard.SetTarget(_spinAngle, ChevronSpin);
        Storyboard.SetTargetProperty(_spinAngle, "Angle");
        _spin.Children.Add(_spinAngle);

        Apply();
    }

    /// <summary>Raised when the user picks a row. Carries the new value.</summary>
    public event EventHandler<string>? ValueChosen;

    /// <summary>The options to offer.</summary>
    public IReadOnlyList<string>? Options
    {
        get => (IReadOnlyList<string>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>The current value, which need not be one of the options.</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>What the closed row reads when there is no value.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>A line icon to draw on every row, when the rows are not countries.</summary>
    public string? ItemIconKind
    {
        get => (string?)GetValue(ItemIconKindProperty);
        set => SetValue(ItemIconKindProperty, value);
    }

    /// <summary>Whether the options are country names and should carry flags.</summary>
    public bool ShowFlags
    {
        get => (bool)GetValue(ShowFlagsProperty);
        set => SetValue(ShowFlagsProperty, value);
    }

    /// <summary>Whether text that matches no option can still be committed.</summary>
    public bool AllowCustom
    {
        get => (bool)GetValue(AllowCustomProperty);
        set => SetValue(AllowCustomProperty, value);
    }

    /// <summary>
    /// The label of the "add" row under the list, or null for no such row.
    /// </summary>
    /// <remarks>
    /// For lists that point at records rather than values - the website an account
    /// belongs to - the drawer can offer to go and create one instead of making the
    /// user cancel out and find the right screen. The label is the whole contract:
    /// what the new record's form should be is the host's business, raised as
    /// <see cref="AddRequested"/>.
    /// </remarks>
    public string? AddLabel
    {
        get => (string?)GetValue(AddLabelProperty);
        set => SetValue(AddLabelProperty, value);
    }

    /// <summary>Raised when the user picks the "add" row. Carries nothing.</summary>
    public event EventHandler? AddRequested;

    private static void OnSurfaceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((ChoicePicker)sender).Apply();

    /// <summary>Draws the closed row: the value or the placeholder, and a flag.</summary>
    private void Apply()
    {
        if (ValueLabel is null)
        {
            return;
        }

        bool hasValue = !string.IsNullOrWhiteSpace(Value);

        ValueLabel.Text = hasValue ? Value : Placeholder;
        ValueLabel.Foreground = Brush(hasValue ? "TextPrimaryBrush" : "SearchPlaceholderBrush");

        bool flag = ShowFlags && hasValue && FlagData.For(Value) is not null;
        PresenterFlag.Country = flag ? Value : null;
        PresenterFlag.Visibility = flag ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPresenterTapped(object sender, TappedRoutedEventArgs args) => Open();

    /// <summary>
    /// Opens on Enter, Space and Down, which is what every other dropdown on the
    /// platform does and therefore what a keyboard user will try.
    /// </summary>
    protected override void OnKeyDown(KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.Down)
        {
            args.Handled = true;
            Open();
            return;
        }

        base.OnKeyDown(args);
    }

    private void Open()
    {
        if (Drawer.IsOpen)
        {
            return;
        }

        FlyoutBase.ShowAttachedFlyout(Presenter);
    }

    private void OnDrawerOpening(object? sender, object args)
    {
        bool filtered = (Options?.Count ?? 0) >= FilterThreshold;

        FilterShell.Visibility = filtered ? Visibility.Visible : Visibility.Collapsed;
        FilterBox.PlaceholderText = ShowFlags ? "Search countries" : "Search";

        // The add row belongs to the hosts that asked for it and to nobody else;
        // the country and gender lists have nothing to add.
        AddRow.Visibility = string.IsNullOrWhiteSpace(AddLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
        AddRowLabel.Text = AddLabel;

        _syncing = true;
        FilterBox.Text = string.Empty;
        _syncing = false;

        // Match the field it drops out of, with a floor so a narrow field still gets
        // a list wide enough to read.
        DrawerBody.Width = Math.Max(Presenter.ActualWidth, 240);

        // Roughly nine rows on a laptop and more on a large display, but never taller
        // than the window can show.
        double available = XamlRoot?.Size.Height ?? 720.0;
        Rows.MaxHeight = Math.Clamp(available * 0.45, 176, 460);

        Build();
        Spin(180);
    }

    private void OnDrawerOpened(object? sender, object args)
    {
        if (FilterShell.Visibility == Visibility.Visible)
        {
            FilterBox.Focus(FocusState.Programmatic);
        }
        else
        {
            Rows.Focus(FocusState.Programmatic);
        }

        if (Rows.SelectedItem is not null)
        {
            Rows.ScrollIntoView(Rows.SelectedItem, ScrollIntoViewAlignment.Leading);
        }
    }

    private void OnDrawerClosed(object? sender, object args) => Spin(0);

    private void OnFilterChanged(object sender, TextChangedEventArgs args)
    {
        if (!_syncing)
        {
            Build();
        }
    }

    /// <summary>
    /// Down walks into the list, Enter takes the first row, Escape gives up.
    /// </summary>
    /// <remarks>
    /// Without this the filter box swallows the arrow keys and the list can only be
    /// reached with the mouse, which for a list of this length is the difference
    /// between typing three letters and scrolling.
    /// </remarks>
    private void OnFilterKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Down:
                args.Handled = true;
                Rows.Focus(FocusState.Programmatic);
                break;

            case VirtualKey.Enter when _rows.Count > 0:
                args.Handled = true;
                Commit(_rows[0]);
                break;

            // Nothing left to commit and a way to add one: Enter should take it,
            // or the row is mouse-only.
            case VirtualKey.Enter when AddRow.Visibility == Visibility.Visible:
                args.Handled = true;
                Drawer.Hide();
                AddRequested?.Invoke(this, EventArgs.Empty);
                break;

            case VirtualKey.Escape:
                args.Handled = true;
                Drawer.Hide();
                break;
        }
    }

    /// <summary>Fills the drawer with the options that match the filter.</summary>
    /// <remarks>
    /// Rows that start with the typed text come before rows that merely contain it, so
    /// typing "ind" offers India and Indonesia before the British Indian Ocean
    /// Territory. Within each group the catalog's own order - alphabetical - survives,
    /// because OrderBy is stable.
    /// </remarks>
    private void Build()
    {
        string query = FilterBox.Text.Trim();
        IReadOnlyList<string> options = Options ?? Array.Empty<string>();

        IEnumerable<string> matches = options;
        if (query.Length > 0)
        {
            matches = options
                .Where(o => o.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1);
        }

        _syncing = true;
        _rows.ClearSafely();

        // An answer the catalog does not have still has to be enterable. The country
        // field has always allowed one; this is where it went.
        bool exact = options.Any(o => string.Equals(o, query, StringComparison.OrdinalIgnoreCase));
        if (AllowCustom && query.Length > 0 && !exact)
        {
            _rows.Add(new ChoiceOption($"Use \u201c{query}\u201d", query, null, "Plus"));
        }

        foreach (string option in matches)
        {
            _rows.Add(new ChoiceOption(
                option,
                option,
                ShowFlags ? option : null,
                ShowFlags ? null : ItemIconKind));
        }

        ChoiceOption? current = _rows.FirstOrDefault(
            r => string.Equals(r.Result, Value, StringComparison.OrdinalIgnoreCase));

        foreach (ChoiceOption row in _rows)
        {
            row.IsChosen = ReferenceEquals(row, current) ? Visibility.Visible : Visibility.Collapsed;
        }

        Rows.SelectedItem = current;
        _syncing = false;

        // The list is left in the tree when it is empty rather than collapsed. A
        // collapsed list that is later cleared is the shape of fault this app has
        // already hit three times, and an empty ListView draws nothing anyway.
        NoMatches.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMatches.Text = query.Length > 0
            ? $"Nothing matches \u201c{query}\u201d."
            : "There is nothing to choose from yet.";
    }

    private void OnRowChosen(object sender, SelectionChangedEventArgs args)
    {
        if (_syncing || Rows.SelectedItem is not ChoiceOption option)
        {
            return;
        }

        Commit(option);
    }

    private void Commit(ChoiceOption option)
    {
        Drawer.Hide();

        if (string.Equals(option.Result, Value, StringComparison.Ordinal))
        {
            return;
        }

        Value = option.Result;
        ValueChosen?.Invoke(this, option.Result);
    }

    /// <summary>
    /// The "add" row: close first, then hand over, so the form that opens next never
    /// has to share the screen with a drawer belonging to a dialog behind it.
    /// </summary>
    private void OnAddRowClick(object sender, RoutedEventArgs args)
    {
        Drawer.Hide();
        AddRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Turns the chevron over as the drawer opens and back as it closes.
    /// </summary>
    /// <remarks>
    /// Both ends are stated rather than read off the transform. A property the
    /// compositor is animating cannot be read back reliably from the UI thread, and
    /// the two angles here are never in doubt.
    /// </remarks>
    private void Spin(double to)
    {
        _spinAngle.From = to > 0 ? 0 : 180;
        _spinAngle.To = to;
        _spin.Begin();
    }

    private static Microsoft.UI.Xaml.Media.Brush Brush(string key)
        => (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[key];
}
