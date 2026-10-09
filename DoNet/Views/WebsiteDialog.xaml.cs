using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using DoNet.Controls;
using DoNet.Models;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;
using DoNet.ViewModels;
using DoNet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.Collections.Specialized;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The modal website dialog. See the XAML for why one control serves all three modes.
/// </summary>
public sealed partial class WebsiteDialog : UserControl
{
    /// <summary>What the payment methods panel is currently asking for.</summary>
    private enum MethodTapMode
    {
        /// <summary>The panel shows its normal editing contents.</summary>
        None,

        /// <summary>Each method is a target; tapping one opens the rename popup.</summary>
        Edit,

        /// <summary>Each method is a target; taps build a selection to delete.</summary>
        Delete,
    }

    private readonly WebsitesViewModel _host;

    private MethodTapMode _methodMode = MethodTapMode.None;

    /// <summary>The methods picked in delete mode, by their offered names.</summary>
    private readonly HashSet<string> _tapSelected = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The method the rename popup is editing, if it is open.</summary>
    private string? _renameTarget;

    public WebsiteDialog()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind binds
        // its root object while the generated code runs.
        _host = App.Current.Services.GetRequiredService<WebsitesViewModel>();
        ViewModel = _host.Dialog;

        InitializeComponent();

        _host.PropertyChanged += OnHostPropertyChanged;
        ViewModel.PropertyChanged += OnDialogPropertyChanged;

        // Named rather than a lambda, because Detach has to be able to take it off.
        _host.PaymentMethodsChanged += OnHostPaymentMethodsChanged;

        // The chips mirror the selection, and the dismiss crosses only exist while
        // editing - so both the collection and the mode have to redraw them.
        // A named handler rather than a lambda, because this one has to be
        // detachable for exactly the reason the two above it are.
        ViewModel.Selected.CollectionChanged += OnSelectedChanged;

        // A deferred control can be created *because* it is already supposed to be
        // showing, in which case the property change that opens it fired before this
        // instance existed. Catch up once we are in the tree - Open touches the visual
        // tree and starts a storyboard, so the constructor is too early.
        Loaded += (_, _) =>
        {
            if (_host.IsDialogOpen && Root.Visibility != Visibility.Visible)
            {
                Open();
            }
        };

        // HomePage is rebuilt on every unlock, and the view model is a singleton, so
        // without this each lock/unlock cycle leaves another detached dialog listening
        // to it. They all react, all try to animate, and the ones no longer in the
        // visual tree throw while doing it.
        Unloaded += (_, _) => Detach();
    }

    public WebsiteDialogViewModel ViewModel { get; }

    private void OnSelectedChanged(object? sender, NotifyCollectionChangedEventArgs args) =>
        RebuildChips();

    /// <summary>
    /// Drops every hold this instance has on the singleton view model.
    /// </summary>
    /// <remarks>
    /// Nulling the repeater's source matters as much as the three event handlers.
    /// An ItemsRepeater subscribes to its collection through its own ItemsSourceView,
    /// which x:Bind knows nothing about, so stopping the bindings would leave a
    /// detached dialog still being told about every change to the payment catalog.
    /// </remarks>
    private void Detach()
    {
        _host.PropertyChanged -= OnHostPropertyChanged;
        ViewModel.PropertyChanged -= OnDialogPropertyChanged;
        ViewModel.Selected.CollectionChanged -= OnSelectedChanged;
        _host.PaymentMethodsChanged -= OnHostPaymentMethodsChanged;
        MethodOptions.ItemsSource = null;
    }



    /// <summary>Animates the switch between preview and edit.</summary>
    private void OnDialogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Only when the dialog is already up. Opening runs its own entrance, and
        // playing both at once makes the panel visibly stutter.
        if (args.PropertyName == nameof(WebsiteDialogViewModel.Mode))
        {
            RebuildChips();

            // Preview hides the options, editing shows them again. A tap mode never
            // survives a mode change: the form behind it has just been rebuilt.
            _methodMode = MethodTapMode.None;
            _tapSelected.Clear();
            SyncTapMode();
        }

        if (args.PropertyName == nameof(WebsiteDialogViewModel.Mode)
            && Root.Visibility == Visibility.Visible
            && Resources["ModeChangeStoryboard"] is Storyboard change)
        {
            change.Begin();
        }
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WebsitesViewModel.IsDialogOpen))
        {
            if (_host.IsDialogOpen)
            {
                Open();
            }
            else if (Root.Visibility == Visibility.Visible)
            {
                Close();
            }
        }
    }

    /// <summary>
    /// Attaches the payment options to the repeater only while it is genuinely on
    /// screen, and detaches them the rest of the time.
    /// </summary>
    /// <remarks>
    /// This repeater is collapsed twice over: the whole dialog is hidden while closed,
    /// and the options are hidden again in preview mode. A collapsed ItemsRepeater has
    /// no measured viewport, so a collection change arriving at one comes back as
    /// COMException: Unspecified error - and ShowAdd, ShowEdit and ShowPreview all
    /// rebuild Choices *before* the dialog is made visible. Reordering that would not
    /// help, because setting Visibility does not measure anything synchronously.
    /// Holding the source only while the repeater can service it is what does.
    /// </remarks>
    private void SyncMethodOptionsSource()
    {
        // The tap-mode term matters as much as the other two. While one is running
        // the options list is hidden, and the host's rename and delete rewrite
        // Choices from under it - the same collection change at the same collapsed
        // repeater this app has already been bitten by three times.
        bool live = Root.Visibility == Visibility.Visible
            && ViewModel.IsEditing
            && _methodMode == MethodTapMode.None;
        MethodOptions.ItemsSource = live ? ViewModel.Choices : null;
    }

    private void Open()
    {
        Root.Visibility = Visibility.Visible;

        // A fresh open starts from the form, never from whatever tap mode was
        // running when the dialog last closed.
        _methodMode = MethodTapMode.None;
        _tapSelected.Clear();
        SyncTapMode();

        if (Resources["OpenStoryboard"] is Storyboard open)
        {
            open.Begin();
        }
    }

    /// <summary>
    /// Plays the exit, then hides. Hiding only after the animation completes is the
    /// whole point - collapsing first would make the close instant and the storyboard
    /// invisible.
    /// </summary>
    private void Close()
    {
        if (Resources["CloseStoryboard"] is Storyboard close)
        {
            close.Begin();
        }
        else
        {
            Root.Visibility = Visibility.Collapsed;
            SyncMethodOptionsSource();
        }
    }

    private void OnCloseCompleted(object? sender, object args)
    {
        Root.Visibility = Visibility.Collapsed;
        RenameOverlay.Visibility = Visibility.Collapsed;
        _methodMode = MethodTapMode.None;
        _tapSelected.Clear();
        SyncMethodOptionsSource();
        SyncTapMode();

        // Reset the transform the exit left behind, or the next open starts displaced.
        CardOffset.Y = 0;
        Card.Opacity = 1;
        Scrim.Opacity = 1;
    }

    /// <summary>Click-away on the backdrop closes, as a modal should.</summary>
    private void OnScrimTapped(object sender, TappedRoutedEventArgs args) => _host.CloseDialog();

    private void OnCloseClick(object sender, RoutedEventArgs args) => _host.CloseDialog();

    private void OnCancelClick(object sender, RoutedEventArgs args) => _host.CloseDialog();

    /// <remarks>
    /// An async void handler that throws takes the process down - there is no caller
    /// to catch it and the framework has nowhere to send it. Every one of them is
    /// wrapped.
    /// </remarks>
    private async void OnPrimaryClick(object sender, RoutedEventArgs args)
    {
        try
        {
            await _host.CommitDialogAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Saving a website failed", error);
        }
    }

    /// <summary>
    /// Releases this control's hold on the view model. Called when the host page is
    /// leaving for good; see <see cref="HomePage.OnNavigatedFrom"/> for why it is not
    /// automatic. Safe to call more than once - Unloaded does the same work.
    /// </summary>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        Detach();
    }


    /// <summary>
    /// Rebuilds the chips whenever the selection changes.
    /// </summary>
    /// <remarks>
    /// Built in code into a WrapPanel rather than bound to an ItemsRepeater, because
    /// chips are as wide as their text and ItemsRepeater has no wrapping layout that
    /// allows variable widths. There are only ever a handful.
    ///
    /// In preview the chips are read-only; while editing each carries a dismiss cross,
    /// which unticks the matching box - the list and the ticks are two views of one
    /// selection, so removing from either has to update both.
    /// </remarks>
    private void RebuildChips()
    {
        SelectedChips.Children.Clear();

        bool any = ViewModel.Selected.Count > 0;

        SelectedChips.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        // The "none selected" line is only worth saying while reading. In the form the
        // empty tick boxes already say it.
        NoMethodsText.Visibility = !any && ViewModel.IsPreview
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!any)
        {
            return;
        }

        foreach (string method in ViewModel.Selected)
        {
            SelectedChips.Children.Add(BuildChip(method, ViewModel.IsEditing));
        }
    }

    private Border BuildChip(string method, bool removable)
    {
        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        content.Children.Add(new Controls.PaymentMethodIcon
        {
            Method = method,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });

        content.Children.Add(new TextBlock
        {
            Text = method,
            FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (removable)
        {
            Button dismiss = new()
            {
                Style = (Style)Application.Current.Resources["BareButtonStyle"],
                Width = 18,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = "\u2715",
                    FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            string captured = method;
            dismiss.Click += (_, _) => ViewModel.Deselect(captured);

            content.Children.Add(dismiss);
        }

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, removable ? 6 : 12, 4),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AddActionBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private void OnAddMethodClick(object sender, RoutedEventArgs args) => CommitNewMethod();

    /// <summary>Enter adds the method, so the button is not the only way.</summary>
    private void OnNewMethodKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        CommitNewMethod();
    }

    private void CommitNewMethod()
    {
        try
        {
            if (ViewModel.CommitNewMethod() is { } added)
            {
                // Only genuinely new methods are worth remembering; the view model
                // returns null when the box was blank or the method already existed.
                _host.RememberMethod(added);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Adding a payment method failed", error);
        }
    }

    // ------------------------------------------------------------------
    // Editing and deleting the methods themselves
    // ------------------------------------------------------------------

    /// <summary>Brings the panel's visibility in line with the current tap mode.</summary>
    private void SyncTapMode()
    {
        bool tap = _methodMode != MethodTapMode.None;

        NormalMethods.Visibility = ViewModel.IsEditing && !tap
            ? Visibility.Visible
            : Visibility.Collapsed;

        // The two discs stay put in the heading while the panel is a form, and step
        // aside while it is asking for taps - the banner says what is happening.
        MethodActionButtons.Visibility = NormalMethods.Visibility;

        TapModePanel.Visibility = tap ? Visibility.Visible : Visibility.Collapsed;
        TapDeleteButton.Visibility = _methodMode == MethodTapMode.Delete
            ? Visibility.Visible
            : Visibility.Collapsed;

        SyncMethodOptionsSource();
    }

    private void OnEditMethodsClick(object sender, RoutedEventArgs args)
    {
        if (_methodMode != MethodTapMode.None)
        {
            return;
        }

        _methodMode = MethodTapMode.Edit;
        _tapSelected.Clear();
        TapModeBanner.Text = "Tap the payment method you want to edit.";
        BuildTapItems();
        SyncTapMode();
    }

    private void OnDeleteMethodsClick(object sender, RoutedEventArgs args)
    {
        if (_methodMode != MethodTapMode.None)
        {
            return;
        }

        _methodMode = MethodTapMode.Delete;
        _tapSelected.Clear();
        TapModeBanner.Text = "Tap the payment methods you want to delete.";
        BuildTapItems();
        UpdateTapDeleteButton();
        SyncTapMode();
    }

    private void OnTapCancelClick(object sender, RoutedEventArgs args) => ExitTapMode();

    /// <summary>Exits the current tap mode, keeping whatever the form had.</summary>
    private void ExitTapMode()
    {
        _methodMode = MethodTapMode.None;
        _tapSelected.Clear();
        SyncTapMode();
    }

    /// <summary>
    /// Fills the tap panel with one pill per offered method.
    /// </summary>
    /// <remarks>
    /// Rebuilt wholesale on every selection change rather than patched in place:
    /// there are never more than a couple of dozen, and a rebuild cannot drift out
    /// of step with the selection the way a patched pill can.
    /// </remarks>
    private void BuildTapItems()
    {
        TapItems.Children.Clear();

        foreach (PaymentMethodChoice choice in ViewModel.Choices)
        {
            bool selected = _methodMode == MethodTapMode.Delete && _tapSelected.Contains(choice.Name);
            TapItems.Children.Add(BuildTapPill(choice.Name, selected));
        }
    }

    private Button BuildTapPill(string method, bool selected)
    {
        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (_methodMode == MethodTapMode.Delete)
        {
            // The tick appears only on a picked pill; on an unpicked one it would be
            // a checkbox nobody asked for.
            content.Children.Add(new LineIcon
            {
                Kind = "Check",
                Width = 13,
                Height = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextOnBrandBrush"],
                Visibility = selected ? Visibility.Visible : Visibility.Collapsed,
            });
        }

        content.Children.Add(new PaymentMethodIcon
        {
            Method = method,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });

        content.Children.Add(new TextBlock
        {
            Text = method,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources[
                selected ? "TextOnBrandBrush" : "TextPrimaryBrush"],
        });

        Button pill = new()
        {
            Style = (Style)Application.Current.Resources["BareButtonStyle"],
            Content = new Border
            {
                CornerRadius = new CornerRadius(16),
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Background = (Brush)Application.Current.Resources[
                    selected ? "DangerBrush" : "CardBackgroundBrush"],
                BorderBrush = (Brush)Application.Current.Resources[
                    selected ? "DangerBrush" : "SearchBorderBrush"],
                BorderThickness = new Thickness(1),
                Child = content,
            },
        };

        string captured = method;
        pill.Click += (_, _) => OnTapPillClick(captured);

        return pill;
    }

    private void OnTapPillClick(string method)
    {
        if (_methodMode == MethodTapMode.Edit)
        {
            StartRename(method);
            return;
        }

        if (_tapSelected.Contains(method))
        {
            _tapSelected.Remove(method);
        }
        else
        {
            _tapSelected.Add(method);
        }

        BuildTapItems();
        UpdateTapDeleteButton();
    }

    /// <summary>
    /// The delete action reads how much is picked, so it has to be told.
    /// </summary>
    private void UpdateTapDeleteButton()
    {
        int count = _tapSelected.Count;

        TapDeleteButton.IsEnabled = count > 0;
        TapDeleteShell.Opacity = count > 0 ? 1 : 0.55;
        TapDeleteLabel.Text = count > 0 ? $"DELETE ({count})" : "DELETE";
    }

    /// <summary>
    /// Hands the selection to the confirmation, which is the only place the damage
    /// is spelled out. The tap mode is left running under it: cancelling the
    /// confirmation returns to the same selection, and confirming is the host's
    /// business to announce.
    /// </summary>
    private async void OnTapDeleteClick(object sender, RoutedEventArgs args)
    {
        if (_tapSelected.Count == 0)
        {
            return;
        }

        try
        {
            await _host.RequestMethodDeleteAsync(_tapSelected.ToList());
        }
        catch (Exception error)
        {
            AppLog.Error("Asking to delete payment methods failed", error);
        }
    }

    /// <summary>
    /// A rename or delete went through: the catalog and the form have both been
    /// rewritten by the host, and this control's part is to stop asking for taps.
    /// </summary>
    private void OnHostPaymentMethodsChanged() => ExitTapMode();

    // ------------------------------------------------------------------
    // The rename popup
    // ------------------------------------------------------------------

    private void StartRename(string method)
    {
        _renameTarget = method;

        RenameBody.Text =
            $"The new name replaces \u201C{method}\u201D here and on every website that lists it.";

        RenameOverlay.Visibility = Visibility.Visible;

        if (Resources["RenameOpenStoryboard"] is Storyboard open)
        {
            open.Begin();
        }

        // Seeded with the current name and selected, so a one-character fix is
        // exactly one keystroke.
        RenameBox.Text = method;
        RenameBox.SelectAll();
        RenameBox.Focus(FocusState.Programmatic);
        UpdateRenameValidity();
    }

    /// <summary>
    /// The save button's state, from what has been typed.
    /// </summary>
    /// <remarks>
    /// A rename that names an existing method is not an error to show - it is simply
    /// nothing to save, so the button says so by going quiet.
    /// </remarks>
    private void UpdateRenameValidity()
    {
        string candidate = RenameBox.Text.Trim();

        bool clashes = ViewModel.Choices
            .Where(c => _renameTarget is null
                || !PaymentMethodCatalog.Matches(c.Name, _renameTarget))
            .Any(c => PaymentMethodCatalog.Matches(c.Name, candidate));

        bool valid = candidate.Length > 0
            && !PaymentMethodCatalog.Matches(candidate, _renameTarget)
            && !clashes;

        RenameSaveButton.IsEnabled = valid;
        RenameSaveShell.Background = (Brush)Application.Current.Resources[
            valid ? "BrandPrimaryBrush" : "BrandPrimaryDisabledBrush"];

        RenameFieldShell.BorderBrush = (Brush)Application.Current.Resources[
            candidate.Length == 0 || valid ? "SearchBorderBrush" : "FieldBorderErrorBrush"];
    }

    private void OnRenameTextChanged(object sender, TextChangedEventArgs args)
        => UpdateRenameValidity();

    private void OnRenameKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Enter)
        {
            args.Handled = true;

            if (RenameSaveButton.IsEnabled)
            {
                CommitRename();
            }
        }
        else if (args.Key == Windows.System.VirtualKey.Escape)
        {
            args.Handled = true;
            RenameOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void OnRenameScrimTapped(object sender, TappedRoutedEventArgs args)
        => RenameOverlay.Visibility = Visibility.Collapsed;

    private void OnRenameCancelClick(object sender, RoutedEventArgs args)
        => RenameOverlay.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Applies the rename. The tap mode is exited before the call, not after: the
    /// host rewrites the form's method list while it runs, and the options list has
    /// to be back on screen and measured by then.
    /// </summary>
    private void OnRenameSaveClick(object sender, RoutedEventArgs args)
    {
        if (!RenameSaveButton.IsEnabled || _renameTarget is null)
        {
            return;
        }

        CommitRename();
    }

    private async void CommitRename()
    {
        string? oldName = _renameTarget;
        string newName = RenameBox.Text.Trim();

        if (oldName is null || newName.Length == 0)
        {
            return;
        }

        RenameOverlay.Visibility = Visibility.Collapsed;
        ExitTapMode();

        try
        {
            await _host.RenamePaymentMethodAsync(oldName, newName);
        }
        catch (Exception error)
        {
            AppLog.Error("Renaming a payment method failed", error);
        }
    }
}
