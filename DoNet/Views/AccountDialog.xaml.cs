using System;
using System.ComponentModel;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The modal account dialog. See the XAML for why one control serves all three modes.
/// </summary>
public sealed partial class AccountDialog : UserControl
{
    private readonly AccountsViewModel _host;

    public AccountDialog()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind binds
        // its root object while the generated code runs.
        _host = App.Current.Services.GetRequiredService<AccountsViewModel>();
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
        Unloaded += (_, _) => Detach();
    }

    public AccountDialogViewModel ViewModel { get; }

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

    /// <summary>Drops every hold this instance has on the singleton view model.</summary>
    private void Detach()
    {
        _host.PropertyChanged -= OnHostPropertyChanged;
        ViewModel.PropertyChanged -= OnDialogPropertyChanged;
    }

    /// <summary>Animates the switch between preview and edit.</summary>
    private void OnDialogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Only when the dialog is already up. Opening runs its own entrance, and
        // playing both at once makes the panel visibly stutter.
        if (args.PropertyName == nameof(AccountDialogViewModel.Mode)
            && Root.Visibility == Visibility.Visible
            && Resources["ModeChangeStoryboard"] is Storyboard change)
        {
            change.Begin();
        }
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(AccountsViewModel.IsDialogOpen))
        {
            return;
        }

        if (_host.IsDialogOpen)
        {
            Open();
        }
        else if (Root.Visibility == Visibility.Visible)
        {
            Close();
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

    private void OnScrimTapped(object sender, TappedRoutedEventArgs args) => _host.CloseDialog();

    private void OnCloseClick(object sender, RoutedEventArgs args) => _host.CloseDialog();

    private void OnCancelClick(object sender, RoutedEventArgs args) => _host.CloseDialog();

    /// <summary>
    /// Preview switches to edit; add and edit commit.
    /// </summary>
    /// <remarks>
    /// An async void handler that throws takes the process down - there is no caller
    /// to receive the exception - so the whole body is wrapped.
    /// </remarks>
    private async void OnPrimaryClick(object sender, RoutedEventArgs args)
    {
        try
        {
            await _host.CommitDialogAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Saving the account failed", error);
        }
    }
}
