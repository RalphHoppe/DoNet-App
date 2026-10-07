using System;
using DoNet.Models;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Views;

/// <summary>
/// The Persons directory, hosted inside the home screen's content surface.
/// </summary>
public sealed partial class PersonsView : UserControl
{
    public PersonsView()
    {
        ViewModel = App.Current.Services.GetRequiredService<PersonsViewModel>();

        InitializeComponent();

        Loaded += OnLoaded;
    }

    public PersonsViewModel ViewModel { get; }

    /// <summary>
    /// Releases this control's compiled bindings. Called when the host page is leaving
    /// for good; see <see cref="HomePage.OnNavigatedFrom"/> for why it is not automatic.
    /// </summary>
    public void ReleaseBindings() => Bindings.StopTracking();


    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            // The store was already opened while the welcome screen was animating, so
            // this is usually just the first page arriving rather than a cold open.
            await ViewModel.LoadAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Loading the directory failed", error);
        }
    }

    private void OnCardOpen(object? sender, Person person) => ViewModel.OpenPreview(person);

    private void OnCardEdit(object? sender, Person person) => ViewModel.OpenEdit(person);

    private void OnCardDelete(object? sender, Person person) => ViewModel.RequestDelete(person);

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
