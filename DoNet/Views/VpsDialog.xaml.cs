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
/// The modal VPS dialog. See the XAML for why one control serves all three
/// modes, and why the system's fields are half of this form rather than a
/// picker's options.
/// </summary>
public sealed partial class VpsDialog : UserControl
{
    private readonly VpsViewModel _host;

    public VpsDialog()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind binds
        // its root object while the generated code runs.
        _host = App.Current.Services.GetRequiredService<VpsViewModel>();
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
        // to it.
        Unloaded += (_, _) =>
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            ViewModel.PropertyChanged -= OnDialogPropertyChanged;
        };
    }

    public VpsDialogViewModel ViewModel { get; }

    /// <summary>
    /// The fields in the order the form reads: the workbook's order, left to right
    /// and top to bottom - who the VPS hangs off, its name, its money, its dates,
    /// its cycle, its node, then the system underneath it.
    /// </summary>
    /// <remarks>
    /// The person dialog's reason, one grid further: TabIndex only compares within
    /// a container, and this form has two of them. The dialog drives the Tab key
    /// itself; the cells still carry TabOrder so the order is also correct if this
    /// handler ever does not run.
    /// </remarks>
    private FieldCell[] FieldsInTabOrder => new[]
    {
        PersonCell, SiteCell, AccountCell, NameCell,
        BalanceCell, BtcCell, RateCell,
        CreatedCell, OutCell, CycleCell, NodeCell, CountryCell, NoteCell,
        OsCell, IpCell, PortCell, UsernameCell,
        UserPasswordCell, RootUserCell, RootPasswordCell,
        CpuCell, MemoryCell, DiskCell,
        BandwidthCell, BandwidthSpeedCell, UploadCell, DownloadCell,
        PingCell, SystemNoteCell,
    };

    /// <summary>
    /// Moves focus field by field in form order, forwards on Tab and backwards on
    /// Shift+Tab. See <see cref="FieldsInTabOrder"/> for why the dialog drives it.
    /// </summary>
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

        // A merged field is two stops: its value, then the unit hanging off it.
        // The hop happens inside the cell so the driver stays a list of fields.
        if (!back && !order[current].UnitOwnsFocus(focused)
            && order[current].UnitFocusTarget() is { } unit)
        {
            if (unit.Focus(FocusState.Keyboard))
            {
                args.Handled = true;
                return;
            }
        }
        else if (back && order[current].UnitOwnsFocus(focused))
        {
            if (order[current].TryFocus())
            {
                args.Handled = true;
                return;
            }
        }

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
        if (args.PropertyName == nameof(VpsDialogViewModel.Mode)
            && Root.Visibility == Visibility.Visible
            && Resources["ModeChangeStoryboard"] is Storyboard change)
        {
            change.Begin();
        }
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(VpsViewModel.IsDialogOpen))
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
    /// whole point - collapsing first would make the close instant and the
    /// storyboard invisible.
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

    /// <summary>
    /// The one wide action: in preview it turns the form into the editor over the
    /// same record; in add and edit it saves.
    /// </summary>
    /// <remarks>
    /// An async void handler that throws takes the process down - there is no caller
    /// to catch it and the framework has nowhere to send it. Every one of them is
    /// wrapped.
    /// </remarks>
    private async void OnPrimaryClick(object sender, RoutedEventArgs args)
    {
        try
        {
            if (ViewModel.IsPreview)
            {
                if (ViewModel.OriginalRecord is { } record)
                {
                    await _host.EditVpsAsync(record);
                }
            }
            else
            {
                await _host.CommitDialogAsync();
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Acting on the VPS form failed", error);
        }
    }

    /// <summary>
    /// Releases this control's compiled bindings. Called when the host page is
    /// leaving for good; see <see cref="HomePage.OnNavigatedFrom"/> for why it is
    /// not automatic.
    /// </summary>
    public void ReleaseBindings() => Bindings.StopTracking();
}
