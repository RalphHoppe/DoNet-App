using System;
using DoNet.Models;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Views;

/// <summary>
/// The Services directory, hosted inside the home screen's content surface.
/// </summary>
public sealed partial class ServicesView : UserControl
{
    public ServicesView()
    {
        ViewModel = App.Current.Services.GetRequiredService<ServicesViewModel>();

        InitializeComponent();

        Loaded += OnLoaded;
    }

    public ServicesViewModel ViewModel { get; }

    /// <summary>
    /// Releases this control's hold on the view model. Called when the host page is
    /// leaving for good; see <see cref="HomePage.OnNavigatedFrom"/> for why it is not
    /// automatic.
    /// </summary>
    /// <remarks>
    /// Stopping the compiled bindings is not enough on its own. An ItemsRepeater
    /// subscribes to its source collection through its own ItemsSourceView, and that
    /// subscription is invisible to x:Bind - so a detached view would keep receiving
    /// collection changes from the singleton view model, forever, while no longer
    /// being in a visual tree. Dropping the source is what actually unhooks it.
    /// </remarks>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        CardRepeater.ItemsSource = null;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            // Usually a no-op: the catalog was fetched while an earlier screen was
            // animating. Joining that load rather than starting a fresh one is what
            // keeps the directory from flashing empty on the way in.
            await ViewModel.EnsureLoadedAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Loading the services directory failed", error);
        }
    }

    private void OnCardOpen(object? sender, Service service) => ViewModel.OpenPreview(service);

    private void OnCardEdit(object? sender, Service service) => ViewModel.OpenEdit(service);

    private void OnCardDelete(object? sender, Service service) => ViewModel.RequestDelete(service);

    /// <summary>
    /// Fetches the next page as the end of the list comes into reach.
    /// </summary>
    /// <remarks>
    /// The catalog is six rows today and this will fire once and find nothing to do.
    /// It is here because this screen is the template for the service records to
    /// come, and a directory that stops paging the day it grows is a bug introduced
    /// on purpose.
    /// </remarks>
    private async void OnCardScrollChanged(object sender, ScrollViewerViewChangedEventArgs args)
    {
        if (sender is not ScrollViewer scroller)
        {
            return;
        }

        double remaining = scroller.ScrollableHeight - scroller.VerticalOffset;

        if (remaining > scroller.ViewportHeight * 0.75)
        {
            return;
        }

        try
        {
            await ViewModel.LoadMoreAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Loading more service types failed", error);
        }
    }
}
