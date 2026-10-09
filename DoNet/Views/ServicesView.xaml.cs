using System;
using System.ComponentModel;
using DoNet.Models;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The Services directory: the type list, and the drill from it into one type's
/// records.
/// </summary>
public sealed partial class ServicesView : UserControl
{
    public ServicesView()
    {
        ViewModel = App.Current.Services.GetRequiredService<ServicesViewModel>();

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;

        // HomePage is rebuilt on every unlock, and the view model is a singleton -
        // without this, each cycle leaves another detached listener behind.
        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public ServicesViewModel ViewModel { get; }

    /// <summary>
    /// Plays the drill when the view model enters or leaves a type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hidden layer is collapsed, not faded under or disabled in place. Grid
    /// and Border are not Controls and have no IsEnabled to disable anything
    /// with, and an invisible layer that still hit-tests would sit over the
    /// screen swallowing clicks; Collapsed is the one state the platform
    /// guarantees is inert - no pointer, no tab stops, no focus.
    /// </para>
    /// <para>
    /// The records layer's repeater source is attached only while it is visible,
    /// and <see cref="UIElement.UpdateLayout"/> runs before the drill-in load can
    /// land a single record: a collection change against a collapsed, unmeasured
    /// ItemsRepeater is the COMException this app has met before, and the layer
    /// starts collapsed, so the measure has to be forced rather than hoped for.
    /// </para>
    /// </remarks>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ServicesViewModel.IsDrilled))
        {
            return;
        }

        if (ViewModel.IsDrilled)
        {
            TypeWrapper.Visibility = Visibility.Collapsed;
            RecordsLayer.Visibility = Visibility.Visible;
            RecordsScreen.AttachSource();
            RecordsLayer.UpdateLayout();
        }
        else
        {
            RecordsScreen.DetachSource();
            RecordsLayer.Visibility = Visibility.Collapsed;
            TypeWrapper.Visibility = Visibility.Visible;
        }

        if (Resources[ViewModel.IsDrilled ? "DrillInStoryboard" : "DrillOutStoryboard"]
                is Storyboard board)
        {
            board.Begin();
        }
    }

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
        RecordsScreen.ReleaseBindings();
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

    /// <summary>
    /// The open action on a type card. The first open of a type is the structure
    /// designer; every later one is its records.
    /// </summary>
    private async void OnCardOpen(object? sender, Service service)
    {
        try
        {
            await ViewModel.OpenTypeAsync(service);
        }
        catch (Exception error)
        {
            AppLog.Error("Opening a service type failed", error);
        }
    }

    private void OnCardEdit(object? sender, Service service) => ViewModel.OpenEdit(service);

    /// <summary>
    /// The delete on a type card asks first - the confirmation says how many
    /// records would go with the type.
    /// </summary>
    private async void OnCardDelete(object? sender, Service service)
    {
        try
        {
            await ViewModel.RequestDeleteAsync(service);
        }
        catch (Exception error)
        {
            AppLog.Error("Asking to delete a service type failed", error);
        }
    }

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
