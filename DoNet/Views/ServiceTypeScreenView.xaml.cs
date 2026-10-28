using System;
using System.ComponentModel;
using DoNet.Models;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DoNet.Views;

/// <summary>
/// One primary service type's screen, in the type's own colours.
/// </summary>
/// <remarks>
/// The shell of the hand-built directory: the header, the search, the ADD
/// button. The records that will fill it arrive with the fields, and until
/// then the surface is empty, as the reference draws it - the buttons that
/// would need records are asleep rather than absent, so the screen is the
/// finished shape with its content pending.
///</remarks>
public sealed partial class ServiceTypeScreenView : UserControl
{
    public ServiceTypeScreenView()
    {
        ViewModel = App.Current.Services.GetRequiredService<ServicesViewModel>();
        Vps = App.Current.Services.GetRequiredService<VpsViewModel>();
        Wallets = App.Current.Services.GetRequiredService<WalletViewModel>();
        NetworkMap = App.Current.Services.GetRequiredService<NetworkMapViewModel>();

        InitializeComponent();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // This view sits in a collapsed layer until its type is drilled in, so
        // the header follows the type by listening - the kind can change before
        // the layer is ever shown.
        Loaded += (_, _) => ApplyHeader();

        Unloaded += (_, _) => ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    public ServicesViewModel ViewModel { get; }

    /// <summary>
    /// The VPS directory: its search fills the header's box and its records
    /// fill the surface while the VPS screen is the one drilled in.
    /// </summary>
    public VpsViewModel Vps { get; }

    /// <summary>The wallets directory, the same job for its own screen.</summary>
    public WalletViewModel Wallets { get; }

    /// <summary>The VPS map: the records laid over the whole earth.</summary>
    public NetworkMapViewModel NetworkMap { get; }

    /// <summary>Guards the search sync so loading a directory's text is not read back as typing.</summary>
    private bool _syncingSearch;

    /// <summary>
    /// The directory behind the drilled-in screen, or null on the asleep types.
    /// </summary>
    private object? ActiveDirectory => ViewModel.SelectedKind switch
    {
        PrimaryServiceType.Vps => Vps,
        PrimaryServiceType.Wallets => Wallets,
        _ => null,
    };

    /// <summary>
    /// Releases this control's hold on the view model. Called by the host when
    /// the home screen is leaving for good.
    /// </summary>
    public void ReleaseBindings()
    {
        // The shell dresses itself by listening to the view model, and the
        // search box is driven from the code-behind, so this view holds no
        // compiled bindings to stop - the release is the subscriptions. The
        // hosted lists release their own holds below.
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        VpsList?.ReleaseBindings();
        WalletsList?.ReleaseBindings();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ServicesViewModel.SelectedKind))
        {
            ApplyHeader();
        }
    }

    /// <summary>
    /// Dresses the shell in the drilled type's colours: the title, the ADD
    /// button's accents, the subtitle's voice.
    /// </summary>
    private void ApplyHeader()
    {
        if (ViewModel.SelectedKind is not { } kind)
        {
            return;
        }

        string name = PrimaryServiceTypes.NameOf(kind);

        TitleText.Text = name.ToUpperInvariant();
        TitleText.Foreground = PrimaryServiceTypes.GradientOf(kind);
        SubtitleText.Text = $"Every {name.ToLowerInvariant()} saved in your vault";

        AddText.Text = $"ADD {name.ToUpperInvariant()}";
        AddText.Foreground = new SolidColorBrush(
            PrimaryServiceTypes.GradientOf(kind).GradientStops[0].Color);
        AddPlusFill.Fill = AddText.Foreground;

        // The VPS and the Wallets have their fields; the other two are still
        // waiting on theirs. Awake is enabled with a working tooltip, asleep
        // keeps the pending one.
        // The map button is the VPS's own: the other types have no node chain
        // to draw yet, so theirs is simply not on offer rather than asleep.
        bool isVps = kind == PrimaryServiceType.Vps;
        MapButton.Visibility = isVps ? Visibility.Visible : Visibility.Collapsed;

        bool awake = kind is PrimaryServiceType.Vps or PrimaryServiceType.Wallets;
        AddRecordButton.IsEnabled = awake;
        ToolTipService.SetToolTip(
            AddRecordButton,
            awake ? $"Add a {name.ToLowerInvariant()} record" : "Records arrive with the next step");

        SearchInput.PlaceholderText = $"SEARCH {name.ToUpperInvariant()}...";

        // The box shows the drilled directory's own search, so switching types
        // switches the text with it rather than carrying one into the other.
        _syncingSearch = true;
        SearchInput.Text = ActiveDirectory switch
        {
            VpsViewModel vps => vps.SearchText,
            WalletViewModel wallets => wallets.SearchText,
            _ => string.Empty,
        };
        _syncingSearch = false;

        if (awake)
        {
            // Deferred on purpose: realizing it here puts the list in the tree
            // inside a drill, where its repeater gets a measured viewport.
            _ = FindName(kind == PrimaryServiceType.Vps ? nameof(VpsList) : nameof(WalletsList));
        }
    }

    /// <summary>
    /// The search box's text, handed to whichever directory is drilled in.
    /// </summary>
    private void OnSearchInputChanged(object sender, RoutedEventArgs args)
    {
        if (_syncingSearch)
        {
            return;
        }

        switch (ActiveDirectory)
        {
            case VpsViewModel vps:
                vps.SearchText = SearchInput.Text;
                break;

            case WalletViewModel wallets:
                wallets.SearchText = SearchInput.Text;
                break;
        }
    }

    /// <summary>
    /// The awake types only: opens the form. The asleep ones never arrive -
    /// the button is disabled underneath them.
    /// </summary>
    /// <summary>
    /// Opens the map. Fire-and-forget is safe: the read inside is wrapped, so
    /// the call either opens the map or logs why not.
    /// </summary>
    private void OnMapClick(object sender, RoutedEventArgs args)
    {
        try
        {
            _ = NetworkMap.OpenAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Opening the VPS map failed", error);
        }
    }

    private async void OnAddRecordClick(object sender, RoutedEventArgs args)
    {
        try
        {
            switch (ViewModel.SelectedKind)
            {
                case PrimaryServiceType.Vps:
                    await Vps.AddVpsAsync();
                    break;

                case PrimaryServiceType.Wallets:
                    await Wallets.AddWalletAsync();
                    break;
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Opening the record form failed", error);
        }
    }
}
