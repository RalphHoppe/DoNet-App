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
    /// fill the surface, for as long as the VPS is the one type with fields.
    /// </summary>
    public VpsViewModel Vps { get; }

    /// <summary>
    /// Releases this control's hold on the view model. Called by the host when
    /// the home screen is leaving for good.
    /// </summary>
    public void ReleaseBindings()
    {
        // The shell dresses itself by listening to the view model; the search
        // box is the one compiled binding, and its tracking stops with the
        // rest. The hosted list releases its own holds below.
        Bindings.StopTracking();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        VpsList?.ReleaseBindings();
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

        // The VPS has its fields; the other three are still waiting on theirs.
        // Awake is enabled with a working tooltip, asleep keeps the pending one.
        bool awake = kind == PrimaryServiceType.Vps;
        AddRecordButton.IsEnabled = awake;
        ToolTipService.SetToolTip(
            AddRecordButton,
            awake ? $"Add a {name.ToLowerInvariant()} record" : "Records arrive with the next step");

        SearchInput.PlaceholderText = $"SEARCH {name.ToUpperInvariant()}...";

        if (awake)
        {
            // Deferred on purpose: realizing it here puts the list in the tree
            // inside a drill, where its repeater gets a measured viewport.
            _ = FindName(nameof(VpsList));
        }
    }

    /// <summary>
    /// The awake types only: opens the form. The asleep ones never arrive -
    /// the button is disabled underneath them.
    /// </summary>
    private async void OnAddRecordClick(object sender, RoutedEventArgs args)
    {
        try
        {
            if (ViewModel.SelectedKind == PrimaryServiceType.Vps)
            {
                await Vps.AddVpsAsync();
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Opening the VPS form failed", error);
        }
    }
}
