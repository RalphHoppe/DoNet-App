using System;
using System.ComponentModel;
using DoNet.ViewModels;
using DoNet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>The delete confirmation that sits over the Persons screen.</summary>
public sealed partial class ConfirmDialog : UserControl
{
    private readonly PersonsViewModel _host;

    public ConfirmDialog()
    {
        _host = App.Current.Services.GetRequiredService<PersonsViewModel>();

        InitializeComponent();

        _host.PropertyChanged += OnHostPropertyChanged;

        // A deferred control can be created *because* it is already supposed to be
        // showing, in which case the property change that opens it fired before this
        // instance existed. Catch up once we are in the tree - Open touches the visual
        // tree and starts a storyboard, so the constructor is too early.
        Loaded += (_, _) =>
        {
            if (_host.IsConfirmingDelete && Root.Visibility != Visibility.Visible)
            {
                SyncToHost();
            }
        };

        // HomePage is rebuilt on every unlock, and the view model is a singleton, so
        // without this each lock/unlock cycle leaves another detached dialog listening
        // to it. They all react, all try to animate, and the ones no longer in the
        // visual tree throw while doing it.
        Unloaded += (_, _) => _host.PropertyChanged -= OnHostPropertyChanged;
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PersonsViewModel.IsConfirmingDelete))
        {
            SyncToHost();
        }
    }

    /// <summary>Brings the control in line with whatever the view model currently says.</summary>
    private void SyncToHost()
    {
        if (_host.IsConfirmingDelete)
        {
            TitleText.Text = _host.ConfirmDeleteTitle;
            BodyText.Text = _host.ConfirmDeleteBody;
            Root.Visibility = Visibility.Visible;

            if (Resources["OpenStoryboard"] is Storyboard open)
            {
                open.Begin();
            }
        }
        else
        {
            Root.Visibility = Visibility.Collapsed;
        }
    }

    private void OnScrimTapped(object sender, TappedRoutedEventArgs args) => _host.CancelDelete();

    private void OnCancelClick(object sender, RoutedEventArgs args) => _host.CancelDelete();

    /// <remarks>
    /// An async void handler that throws takes the process down - there is no caller
    /// to catch it and the framework has nowhere to send it. Every one of them is
    /// wrapped.
    /// </remarks>
    private async void OnConfirmClick(object sender, RoutedEventArgs args)
    {
        try
        {
            await _host.ConfirmDeleteAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Deleting a person failed", error);
        }
    }
}
