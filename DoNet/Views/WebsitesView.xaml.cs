using System;
using DoNet.Models;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Views;

/// <summary>
/// The Websites directory, hosted inside the home screen's content surface.
/// </summary>
public sealed partial class WebsitesView : UserControl
{
    public WebsitesView()
    {
        ViewModel = App.Current.Services.GetRequiredService<WebsitesViewModel>();

        InitializeComponent();

        Loaded += OnLoaded;
    }

    public WebsitesViewModel ViewModel { get; }

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
            // Usually a no-op: the records were fetched while an earlier screen was
            // animating. Joining that load rather than starting a fresh one is what
            // keeps the directory from flashing empty on the way in.
            await ViewModel.EnsureLoadedAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Loading the websites directory failed", error);
        }
    }

    private void OnCardOpen(object? sender, Website website) => ViewModel.OpenPreview(website);

    private void OnCardEdit(object? sender, Website website) => ViewModel.OpenEdit(website);

    private void OnCardDelete(object? sender, Website website) => ViewModel.RequestDelete(website);

    /// <summary>
    /// Fetches the next page as the end of the list comes into reach.
    /// </summary>
    /// <remarks>
    /// Three quarters of a viewport of slack, so the records are already there by the
    /// time the user arrives rather than appearing under them. Firing on intermediate
    /// scroll events as well as settled ones is deliberate - waiting for the scroll to
    /// stop would make a fast flick hit the bottom and wait.
    ///
    /// When the content is shorter than the viewport the remaining distance is zero and
    /// this fires immediately, which is what fills the first screen.
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
            AppLog.Error("Loading more records failed", error);
        }
    }
}
