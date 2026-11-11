using System;
using System.ComponentModel;
using DoNet.Controls;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;
using DoNet.ViewModels;
using DoNet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The modal person dialog. See the XAML for why one control serves all three modes.
/// </summary>
public sealed partial class PersonDialog : UserControl
{
    private readonly PersonsViewModel _host;

    public PersonDialog()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind binds
        // its root object while the generated code runs.
        _host = App.Current.Services.GetRequiredService<PersonsViewModel>();
        ViewModel = _host.Dialog;

        InitializeComponent();

        _host.PropertyChanged += OnHostPropertyChanged;
        ViewModel.PropertyChanged += OnDialogPropertyChanged;

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
        Unloaded += (_, _) =>
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            ViewModel.PropertyChanged -= OnDialogPropertyChanged;
        };
    }

    public PersonDialogViewModel ViewModel { get; }

    /// <summary>
    /// The fields in the order a person fills them in, which is the order the schema
    /// defines them in: who they are, where they are, how to reach them, then the
    /// recovery block.
    /// </summary>
    /// <remarks>
    /// This exists because XAML cannot express it. Tab order comes from TabIndex, but
    /// TabIndex is only compared <i>within a container</i> - FrameworkElement's
    /// TabFocusNavigation defaults to Local - and the three columns are separate
    /// panels. So tabbing walked one panel top to bottom and only then moved to the
    /// next, which is why First Name led to Gender rather than Last Name.
    ///
    /// The order below zigzags between the left and middle columns, because the design
    /// puts Last Name under the record number on the left while First Name heads the
    /// middle. No arrangement of TabIndex can interleave two containers, so the dialog
    /// drives the Tab key itself.
    ///
    /// Cells still carry a matching TabOrder. It cannot cross columns, but it keeps
    /// each column internally correct, so the built-in behaviour stays sensible if
    /// this handler ever does not run.
    /// </remarks>
    private FieldCell[] FieldsInTabOrder => new[]
    {
        FirstNameCell, LastNameCell, GenderCell, DobCell,
        CountryCell, StateCell, CityCell, StreetCell,
        PostalCodeCell, PhoneCell,
        EmailCell, EmailPasswordCell,
        RecoveryEmailCell, RecoveryPasswordCell, RecoveryWordsCell,
        NoteCell,
    };

    /// <summary>
    /// Moves focus field by field in schema order, forwards on Tab and backwards on
    /// Shift+Tab.
    /// </summary>
    /// <remarks>
    /// Preview rather than the bubbling event, so the decision is made before the
    /// focus manager acts on it.
    ///
    /// Deliberately does not trap focus. Tabbing past the last field, or back past the
    /// first, is left unhandled so the framework carries on to the buttons and out of
    /// the dialog - a form you cannot tab out of is worse than one that tabs oddly.
    /// Focus that is not in a field at all is left alone for the same reason.
    /// </remarks>
    private void OnFieldKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Tab)
        {
            return;
        }

        FieldCell[] order = FieldsInTabOrder;
        object? focused = FocusManager.GetFocusedElement(XamlRoot);

        int current = Array.FindIndex(order, cell => cell.OwnsFocus(focused));

        if (current < 0)
        {
            return;
        }

        bool back = InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(CoreVirtualKeyStates.Down);

        int next = current + (back ? -1 : 1);

        if (next < 0 || next >= order.Length)
        {
            return;
        }

        if (order[next].TryFocus())
        {
            args.Handled = true;
        }
    }


    /// <summary>Animates the switch between preview and edit.</summary>
    private void OnDialogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Only when the dialog is already up. Opening runs its own entrance, and
        // playing both at once makes the panel visibly stutter.
        if (args.PropertyName == nameof(PersonDialogViewModel.Mode)
            && Root.Visibility == Visibility.Visible
            && Resources["ModeChangeStoryboard"] is Storyboard change)
        {
            change.Begin();
        }
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PersonsViewModel.IsDialogOpen))
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

    private void Open()
    {
        Root.Visibility = Visibility.Visible;

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
        }
    }

    private void OnCloseCompleted(object? sender, object args)
    {
        Root.Visibility = Visibility.Collapsed;

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
            AppLog.Error("Saving a person failed", error);
        }
    }

    /// <summary>
    /// Releases this control's compiled bindings. Called when the host page is leaving
    /// for good; see <see cref="HomePage.OnNavigatedFrom"/> for why it is not automatic.
    /// </summary>
    public void ReleaseBindings() => Bindings.StopTracking();

}
