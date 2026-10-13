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
/// The v2 Services directory: the four primary types, and the drill from the
/// type list into one type's hand-built screen.
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
    /// Releases this control's hold on the view model. Called when the host page
    /// is leaving for good; see <see cref="HomePage.OnNavigatedFrom"/> for why it
    /// is not automatic.
    /// </summary>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        TypeScreen.ReleaseBindings();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            // The store warm-up and the catalog read this drives keep the dynamic
            // machinery alive for the round that brings custom types back.
            _ = ViewModel.EnsureLoadedAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Loading the services directory failed", error);
        }
    }

    /// <summary>
    /// Plays the drill when the view model enters or leaves a type, and keeps
    /// the type list's search honest over its four names.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ServicesViewModel.IsDrilled))
        {
            bool drilled = ViewModel.IsDrilled;

            TypeWrapper.Visibility = drilled ? Visibility.Collapsed : Visibility.Visible;
            ScreenLayer.Visibility = drilled ? Visibility.Visible : Visibility.Collapsed;

            if (Resources[drilled ? "DrillInStoryboard" : "DrillOutStoryboard"]
                    is Storyboard board)
            {
                board.Begin();
            }
        }
        else if (args.PropertyName == nameof(ServicesViewModel.SearchText))
        {
            ApplySearch();
        }
    }

    // ------------------------------------------------------------------
    // The type list
    // ------------------------------------------------------------------

    private void OnVpsClick(object sender, RoutedEventArgs args) => ViewModel.OpenPrimary(PrimaryServiceType.Vps);

    private void OnProxyClick(object sender, RoutedEventArgs args) => ViewModel.OpenPrimary(PrimaryServiceType.Proxy);

    private void OnDomainClick(object sender, RoutedEventArgs args) => ViewModel.OpenPrimary(PrimaryServiceType.Domain);

    private void OnWalletsClick(object sender, RoutedEventArgs args) => ViewModel.OpenPrimary(PrimaryServiceType.Wallets);

    /// <summary>
    /// A mark brightens under the pointer - an affordance, not a state change.
    /// </summary>
    private void OnTypePointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (sender is Button button)
        {
            button.Opacity = 0.8;
        }
    }

    private void OnTypePointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (sender is Button button)
        {
            button.Opacity = 1;
        }
    }

    /// <summary>
    /// Filters the four marks by the search box, over their names.
    /// </summary>
    /// <remarks>
    /// Four fixed names are matched in the view: there is no page to fetch and
    /// nothing to debounce, and the list is the whole answer.
    /// </remarks>
    private void ApplySearch()
    {
        string term = ViewModel.SearchText.Trim();

        (Button button, string name)[] marks =
        [
            (VpsButton, PrimaryServiceTypes.NameOf(PrimaryServiceType.Vps)),
            (ProxyButton, PrimaryServiceTypes.NameOf(PrimaryServiceType.Proxy)),
            (DomainButton, PrimaryServiceTypes.NameOf(PrimaryServiceType.Domain)),
            (WalletsButton, PrimaryServiceTypes.NameOf(PrimaryServiceType.Wallets)),
        ];

        bool any = false;

        foreach ((Button button, string name) in marks)
        {
            bool matches = term.Length == 0
                || name.Contains(term, StringComparison.OrdinalIgnoreCase);
            button.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            any |= matches;
        }

        NoMatchesText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
    }
}
