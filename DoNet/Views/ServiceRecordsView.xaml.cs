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
/// The host layer is collapsed between drills, and this view hands its card
/// list's source over only while the layer is visible - <see cref="AttachSource"/>
/// and <see cref="DetachSource"/> are the whole contract, and the reason is the
/// one in their remarks: a collection change reaching a collapsed, unmeasured
/// ItemsRepeater is a crash, and a detached one cannot hear it.
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
    /// Hands the card list its source, as the layer becomes visible.
    /// </summary>
    /// <remarks>
    /// Called by the host after making the layer visible and before the drill-in
    /// load lands: the source is attached first, the layer is measured next (the
    /// host forces a layout pass), and only then can a record arrive.
    /// </remarks>
    public void AttachSource() => CardRepeater.ItemsSource = ViewModel.Records;

    /// <summary>
    /// Drops the card list's source, as the layer is hidden.
    /// </summary>
    /// <remarks>
    /// A detached repeater hears no collection change. The layer is collapsed
    /// while hidden - so its repeater is also unmeasured, and a collection change
    /// against an unmeasured ItemsRepeater is the COMException this app has met
    /// before. The source stays detached for exactly as long as the layer is
    /// invisible.
    /// </remarks>
    public void DetachSource() => CardRepeater.ItemsSource = null;

    /// <summary>
    /// Releases this control's hold on the view model. Called by the host when
    /// the home screen is leaving for good.
    /// </summary>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        DetachSource();
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
