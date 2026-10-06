using System.ComponentModel;
using DoNet.ViewModels;
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

        // HomePage is rebuilt on every unlock, and the view model is a singleton, so
        // without this each lock/unlock cycle leaves another detached dialog listening
        // to it. They all react, all try to animate, and the ones no longer in the
        // visual tree throw while doing it.
        Unloaded += (_, _) => _host.PropertyChanged -= OnHostPropertyChanged;
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(PersonsViewModel.IsConfirmingDelete))
        {
            return;
        }

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

    private async void OnConfirmClick(object sender, RoutedEventArgs args)
        => await _host.ConfirmDeleteAsync();
}
