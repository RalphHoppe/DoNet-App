using System;
using System.ComponentModel;
using DoNet.Controls;
using DoNet.Models;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Views;

/// <summary>
/// One service type's records, as a directory screen in its own right.
/// </summary>
/// <remarks>
/// The host keeps this view out of the tree until its type is drilled in, and
/// drops it again on the way back out - so Loaded is a reliable "we just arrived"
/// and there is no invisible second list to keep alive.
/// </remarks>
public sealed partial class ServiceRecordsView : UserControl
{
    public ServiceRecordsView()
    {
        ViewModel = App.Current.Services.GetRequiredService<ServicesViewModel>();

        InitializeComponent();

        // This view sits in the tree from the moment the Services tab is built,
        // not from the drill-in: Loaded fires long before a type is opened, so
        // the header follows the type by listening rather than by one call.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ServicesViewModel ViewModel { get; }

    /// <summary>
    /// Releases this control's hold on the view model. Called by the host when
    /// the home screen is leaving for good.
    /// </summary>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        CardRepeater.ItemsSource = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ServicesViewModel.SelectedType)
            or nameof(ServicesViewModel.IsDrilled))
        {
            ApplyHeader();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        // The drill-in usually loaded the records before this layer was even
        // shown; joining that load is what keeps the screen from flashing empty
        // on the way in. No-op until a type is drilled in.
        _ = ViewModel.EnsureRecordsLoadedAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        // The host page is being torn down; drop every hold on the singleton.
        ReleaseBindings();
    }

    /// <summary>
    /// The type's name, as the section title. Bound in code because the name
    /// arrives with the drill-in, after this view exists.
    /// </summary>
    private void ApplyHeader()
    {
        TitleText.Text = ViewModel.SelectedType is { } type
            ? type.DisplayName.ToUpperInvariant()
            : "SERVICES";
    }

    /// <summary>
    /// Hands a card the type's structure and relationship options.
    /// </summary>
    /// <remarks>
    /// They belong to the type, not the record, so they cannot ride in on the
    /// record the template binds to - and recycled cards are new cards, each of
    /// which has to be told again.
    /// </remarks>
    private void OnRecordCardLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ServiceRecordCard card)
        {
            card.Structure = ViewModel.Structure;
            card.Choices = ViewModel.Choices;
        }
    }

    private async void OnCardOpen(object? sender, ServiceRecord record)
    {
        try
        {
            await ViewModel.OpenRecordPreviewAsync(record);
        }
        catch (Exception error)
        {
            AppLog.Error("Opening a service record failed", error);
        }
    }

    private async void OnCardEdit(object? sender, ServiceRecord record)
    {
        try
        {
            await ViewModel.OpenRecordEditAsync(record);
        }
        catch (Exception error)
        {
            AppLog.Error("Opening a service record for edit failed", error);
        }
    }

    private void OnCardDelete(object? sender, ServiceRecord record)
        => ViewModel.RequestDeleteRecord(record);

    /// <summary>
    /// Fetches the next page as the end of the list comes into reach.
    /// </summary>
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
            await ViewModel.LoadMoreRecordsAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Loading more service records failed", error);
        }
    }
}
