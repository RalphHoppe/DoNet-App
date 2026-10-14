using System;
using System.ComponentModel;
using DoNet.Models;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Views;

/// <summary>
/// The VPS records list on the type screen - see the XAML for why the records
/// sit straight on the plate.
/// </summary>
/// <remarks>
/// The list is realized once, when the VPS type is first drilled in, and then
/// rides the screen layer's visibility. Its repeater's source is attached only
/// while the layer is shown, with a layout pass forced before the first record
/// can arrive: a collection change against a collapsed, unmeasured
/// <c>ItemsRepeater</c> is the COMException this app has met before, and this
/// view is the one place the VPS directory could meet it again.
///</remarks>
public sealed partial class VpsRecordsView : UserControl
{
    private readonly ServicesViewModel _services;

    private bool _sourceAttached;

    public VpsRecordsView()
    {
        _services = App.Current.Services.GetRequiredService<ServicesViewModel>();
        ViewModel = App.Current.Services.GetRequiredService<VpsViewModel>();

        InitializeComponent();

        // The drill is the Services view's to play; this list follows it, so the
        // records arrive with the layer that carries them rather than before it.
        _services.PropertyChanged += OnServicesPropertyChanged;

        Loaded += OnLoaded;
        Unloaded += (_, _) => _services.PropertyChanged -= OnServicesPropertyChanged;
    }

    public VpsViewModel ViewModel { get; }

    /// <summary>
    /// Releases this control's compiled bindings. Called by the type screen when
    /// the home page is leaving for good.
    /// </summary>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        _services.PropertyChanged -= OnServicesPropertyChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_services.IsDrilled && _services.SelectedKind == PrimaryServiceType.Vps)
        {
            Show();
        }
    }

    /// <summary>
    /// Follows the drill: attached and loading while the VPS screen is on show,
    /// detached and collapsed when it is not.
    /// </summary>
    private void OnServicesPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ServicesViewModel.IsDrilled))
        {
            return;
        }

        if (_services.IsDrilled && _services.SelectedKind == PrimaryServiceType.Vps)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    /// <summary>
    /// Shows the list and starts its first load, in that order: the layout pass
    /// between them is what gives the repeater a viewport to take records into.
    /// </summary>
    private void Show()
    {
        Visibility = Visibility.Visible;
        UpdateLayout();

        if (!_sourceAttached)
        {
            CardRepeater.ItemsSource = ViewModel.Records;
            _sourceAttached = true;
        }

        try
        {
            _ = ViewModel.EnsureLoadedAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Loading the VPS directory failed", error);
        }
    }

    /// <summary>Detaches the source and collapses; the records stop arriving.</summary>
    private void Hide()
    {
        if (_sourceAttached)
        {
            CardRepeater.ItemsSource = null;
            _sourceAttached = false;
        }

        Visibility = Visibility.Collapsed;
    }

    /// <summary>Fetches the next page as the end of the list comes into reach.</summary>
    private void OnCardScrollChanged(object sender, ScrollViewerViewChangedEventArgs args)
    {
        if (args.IsIntermediate)
        {
            return;
        }

        if (CardScroller.VerticalOffset + CardScroller.ViewportHeight >= CardScroller.ExtentHeight - 240)
        {
            _ = ViewModel.LoadMoreAsync();
        }
    }

    private void OnCardOpen(object sender, VpsRecord record)
    {
        // Fire-and-forget is safe here: every store read inside is wrapped, so
        // the call cannot throw - it either opens the preview or logs why not.
        _ = ViewModel.PreviewRecordAsync(record);
    }

    private void OnCardEdit(object sender, VpsRecord record) => _ = ViewModel.EditVpsAsync(record);

    private void OnCardDelete(object sender, VpsRecord record) => ViewModel.RequestDelete(record);
}
