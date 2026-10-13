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
/// button, the empty surface. The records that will fill it arrive with the
/// fields, and until then the surface says so - the buttons that would need
/// them are asleep rather than absent, so the screen is the finished shape
/// with its content pending.
///</remarks>
public sealed partial class ServiceTypeScreenView : UserControl
{
    public ServiceTypeScreenView()
    {
        ViewModel = App.Current.Services.GetRequiredService<ServicesViewModel>();

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
    /// Releases this control's hold on the view model. Called by the host when
    /// the home screen is leaving for good.
    /// </summary>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
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

        EmptyTitle.Text = $"No {name.ToLowerInvariant()} records yet";
    }

    private void OnAddRecordClick(object sender, RoutedEventArgs args)
    {
        // Asleep until the fields arrive; the click handler exists so the
        // button's shape is already the finished one.
        _ = sender;
    }
}
