using System;
using System.ComponentModel;
using DoNet.Services;
using Microsoft.UI.Dispatching;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace DoNet.Views;

/// <summary>
/// The home screen: a navigation rail on the left and, for now, an empty content
/// surface on the right.
/// </summary>
public sealed partial class HomePage : Page
{
    /// <summary>How far a section slides as it leaves and as it arrives, in pixels.</summary>
    private const double SectionTravel = 14;

    /// <summary>Every rail destination, in rail order - which is what gives the
    /// transition its direction.</summary>
    private static readonly HomeSection[] AllSections =
    {
        HomeSection.Services,
        HomeSection.Persons,
        HomeSection.Sites,
        HomeSection.Accounts,
    };

    private readonly PersonsViewModel _persons;
    private readonly WebsitesViewModel _websites;
    private readonly AccountsViewModel _accounts;
    private readonly ServicesViewModel _services;

    // Opacity is linear and movement is eased, which is how the rail and the entrance
    // animation already behave. Out is short enough to read as the old section getting
    // out of the way rather than as a wait; in is long enough to be seen.
    private readonly DoubleAnimation _sectionFadeOut = new()
    {
        From = 1,
        To = 0,
        Duration = Seconds(0.12),
    };

    private readonly DoubleAnimation _sectionSlideOut = new()
    {
        From = 0,
        Duration = Seconds(0.12),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
    };

    private readonly DoubleAnimation _sectionFadeIn = new()
    {
        From = 0,
        To = 1,
        Duration = Seconds(0.22),
    };

    private readonly DoubleAnimation _sectionSlideIn = new()
    {
        To = 0,
        Duration = Seconds(0.22),
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
    };

    private readonly Storyboard _sectionOut = new();
    private readonly Storyboard _sectionIn = new();

    private HomeSection _shownSection = HomeSection.Persons;
    private bool _sectionBusy;
    private bool _introDone;
    private bool _warmUpScheduled;

    public HomePage()
    {
        // Before InitializeComponent, not after: x:Bind resolves its root object while
        // the generated code runs, so a view model assigned afterwards would leave every
        // binding on this page pointing at null.
        ViewModel = App.Current.Services.GetRequiredService<HomeViewModel>();

        _persons = App.Current.Services.GetRequiredService<PersonsViewModel>();
        _websites = App.Current.Services.GetRequiredService<WebsitesViewModel>();
        _accounts = App.Current.Services.GetRequiredService<AccountsViewModel>();
        _services = App.Current.Services.GetRequiredService<ServicesViewModel>();

        InitializeComponent();

        // The modals are deferred, so nothing is listening for the request to open one
        // until they exist. This page watches on their behalf and realises them.
        _persons.PropertyChanged += OnPersonsPropertyChanged;
        _websites.PropertyChanged += OnWebsitesPropertyChanged;
        _accounts.PropertyChanged += OnAccountsPropertyChanged;
        _services.PropertyChanged += OnServicesPropertyChanged;
        ViewModel.PropertyChanged += OnSectionChanged;

        WireSectionTransition();

        // Match the surface to the view model rather than trusting the two to agree.
        // They do today - both open on Persons - but the grids' visibility is no longer
        // bound to anything, so nothing else would correct it if that ever changed.
        ApplySection();

        Loaded += OnLoaded;
    }

    public HomeViewModel ViewModel { get; }

    private static Duration Seconds(double value) => new(TimeSpan.FromSeconds(value));

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (Resources["IntroStoryboard"] is Storyboard intro)
        {
            // Build the modals once the entrance has played. Doing it before would put
            // roughly 1,700 element constructions on the UI thread while an animation
            // is running on it, which is exactly the stutter deferring them avoids.
            if (!_warmUpScheduled)
            {
                _warmUpScheduled = true;
                intro.Completed += OnIntroFinished;
            }

            intro.Begin();
        }
        else
        {
            _introDone = true;
            QueueModalWarmUp();
        }
    }

    private void OnIntroFinished(object? sender, object args)
    {
        // Section transitions stay switched off until the entrance has finished. A tab
        // change during it would be two animations on the same subtree fighting over
        // the same opacity, and the entrance would lose.
        _introDone = true;
        QueueModalWarmUp();
    }

    /// <summary>
    /// Builds the deferred modals during idle time, so the first person a user opens
    /// does not pay for it.
    /// </summary>
    /// <remarks>
    /// Low priority: the queue drains this only when there is nothing else to do, so
    /// typing, scrolling and animation all come first. It is a warm-up, not a
    /// requirement - <see cref="OnPersonsPropertyChanged"/> realises either modal
    /// immediately if one is needed before this runs.
    /// </remarks>
    private void QueueModalWarmUp()
        => DispatcherQueue.TryEnqueue(
            DispatcherQueuePriority.Low,
            () =>
            {
                Realize("PersonModal");
                Realize("WebsiteModal");
                Realize("AccountModal");
                Realize("ServiceModal");
                Realize("ConfirmModal");

                // The Persons page was preloaded on the welcome screen; this is the
                // matching window for the other directories, so the rail's first trip
                // to any of them finds its records already there. Cheap when it is
                // not needed - the store is open by now, so it is one query.
                _websites.PreloadAsync().Observe("Preloading the websites directory");
                _accounts.PreloadAsync().Observe("Preloading the accounts directory");
                _services.PreloadAsync().Observe("Preloading the services directory");
            });

    private void OnSectionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(HomeViewModel.SelectedSection))
        {
            return;
        }

        BeginSectionTransition();
    }

    /// <summary>
    /// Points the four animations at the section layer. Done here rather than in XAML
    /// because a Storyboard given a target object needs no namescope, and two of the
    /// three sections do not exist when the page is parsed.
    /// </summary>
    private void WireSectionTransition()
    {
        Storyboard.SetTarget(_sectionFadeOut, SectionHost);
        Storyboard.SetTargetProperty(_sectionFadeOut, "Opacity");
        Storyboard.SetTarget(_sectionFadeIn, SectionHost);
        Storyboard.SetTargetProperty(_sectionFadeIn, "Opacity");

        Storyboard.SetTarget(_sectionSlideOut, SectionShift);
        Storyboard.SetTargetProperty(_sectionSlideOut, "Y");
        Storyboard.SetTarget(_sectionSlideIn, SectionShift);
        Storyboard.SetTargetProperty(_sectionSlideIn, "Y");

        _sectionOut.Children.Add(_sectionFadeOut);
        _sectionOut.Children.Add(_sectionSlideOut);
        _sectionIn.Children.Add(_sectionFadeIn);
        _sectionIn.Children.Add(_sectionSlideIn);

        _sectionOut.Completed += OnSectionHidden;
        _sectionIn.Completed += OnSectionShown;
    }

    /// <summary>
    /// Starts the move from one rail destination to the next.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old section leaves in the direction of travel and the new one arrives from
    /// the opposite side, so going down the rail moves the content up and going back up
    /// moves it down. That is what makes the motion say which way the rail went rather
    /// than only that something changed.
    /// </para>
    /// <para>
    /// A transition in flight is never interrupted. A second click during one is
    /// recorded by the view model and chased from <see cref="OnSectionShown"/>, which
    /// costs at most a third of a second and is worth it: an animation restarted from a
    /// value the UI thread cannot reliably read mid-flight - these run on the
    /// compositor - is how a clean fade turns into a flash.
    /// </para>
    /// </remarks>
    private void BeginSectionTransition()
    {
        HomeSection target = ViewModel.SelectedSection;
        if (target == _shownSection)
        {
            return;
        }

        // Build the incoming grid now, while nothing is moving. Building it at the
        // midpoint instead would put a few hundred element constructions on the UI
        // thread in the middle of the transition, and no amount of easing hides that.
        PrepareArrival(SectionElement(target, create: true));

        if (!_introDone)
        {
            ApplySection();
            return;
        }

        if (_sectionBusy)
        {
            return;
        }

        StartSectionOut();
    }

    private void StartSectionOut()
    {
        _sectionBusy = true;
        _sectionSlideOut.To = -SectionTravel * Direction();
        _sectionOut.Begin();
    }

    private void OnSectionHidden(object? sender, object args)
    {
        // Direction first: ApplySection is what moves _shownSection on.
        double arriveFrom = SectionTravel * Direction();

        // The layer is invisible at this point, so the swap costs nothing to look at.
        ApplySection();

        _sectionSlideIn.From = arriveFrom;
        _sectionIn.Begin();
    }

    private void OnSectionShown(object? sender, object args)
    {
        _sectionBusy = false;

        // The rail moved again while this was playing.
        if (ViewModel.SelectedSection != _shownSection)
        {
            StartSectionOut();
        }
    }

    /// <summary>+1 when the rail moved down its list of destinations, -1 when up.</summary>
    private int Direction() => ViewModel.SelectedSection > _shownSection ? 1 : -1;

    /// <summary>Shows the selected section and collapses the rest.</summary>
    private void ApplySection()
    {
        HomeSection section = ViewModel.SelectedSection;

        foreach (HomeSection candidate in AllSections)
        {
            UIElement? element = SectionElement(candidate, create: candidate == section);
            if (element is null)
            {
                continue;
            }

            bool selected = candidate == section;
            element.Opacity = selected ? 1 : 0;
            element.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }

        _shownSection = section;
    }

    /// <summary>
    /// The grid behind a rail destination, built on request and never otherwise.
    /// </summary>
    /// <remarks>
    /// Only the section being shown is ever passed <paramref name="create"/>. The two
    /// deferred grids must not be built ahead of time: their records are preloaded
    /// during idle, and an ItemsRepeater whose collection changes while it sits in a
    /// subtree that has never been measured throws. Built on the way in, it is measured
    /// moments later.
    /// </remarks>
    private UIElement? SectionElement(HomeSection section, bool create)
    {
        switch (section)
        {
            case HomeSection.Services:
                if (create && ServicesGrid is null)
                {
                    Realize("ServicesGrid");
                }

                return ServicesGrid;

            case HomeSection.Persons:
                return DirectoryView;

            case HomeSection.Sites:
                if (create && SitesView is null)
                {
                    Realize("SitesView");
                }

                return SitesView;

            case HomeSection.Accounts:
                if (create && AccountsGrid is null)
                {
                    Realize("AccountsGrid");
                }

                return AccountsGrid;

            default:
                return null;
        }
    }

    /// <summary>
    /// Puts the incoming grid into the layout at the start of the transition, with
    /// nothing to see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transparent rather than collapsed, and this is the whole point of the method. A
    /// collapsed element is never measured, and an ItemsRepeater that has never been
    /// measured throws on any change to its collection - so a grid left collapsed for
    /// the length of the transition would be a live crash window for anything that
    /// finished loading inside it. Visible at zero opacity is laid out like any other
    /// element while showing exactly as much as collapsed does.
    /// </para>
    /// <para>
    /// Opacity before visibility, so there is no ordering in which a frame could catch
    /// it drawn over the section it is replacing.
    /// </para>
    /// </remarks>
    private static void PrepareArrival(UIElement? element)
    {
        if (element is not null)
        {
            element.Opacity = 0;
            element.Visibility = Visibility.Visible;
        }
    }

    private void OnServicesPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ServicesViewModel.IsDialogOpen) && _services.IsDialogOpen)
        {
            Realize("ServiceModal");
        }
        else if (args.PropertyName == nameof(ServicesViewModel.IsConfirmingDelete)
                 && _services.IsConfirmingDelete)
        {
            Realize("ConfirmModal");
        }
    }

    private void OnAccountsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(AccountsViewModel.IsDialogOpen) && _accounts.IsDialogOpen)
        {
            Realize("AccountModal");
        }
        else if (args.PropertyName == nameof(AccountsViewModel.IsConfirmingDelete)
                 && _accounts.IsConfirmingDelete)
        {
            Realize("ConfirmModal");
        }
    }

    private void OnWebsitesPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WebsitesViewModel.IsDialogOpen) && _websites.IsDialogOpen)
        {
            Realize("WebsiteModal");
        }
        else if (args.PropertyName == nameof(WebsitesViewModel.IsConfirmingDelete)
                 && _websites.IsConfirmingDelete)
        {
            Realize("ConfirmModal");
        }
    }

    private void OnPersonsPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PersonsViewModel.IsDialogOpen) && _persons.IsDialogOpen)
        {
            Realize("PersonModal");
        }
        else if (args.PropertyName == nameof(PersonsViewModel.IsConfirmingDelete)
                 && _persons.IsConfirmingDelete)
        {
            Realize("ConfirmModal");
        }
    }

    /// <summary>
    /// Forces a deferred element into existence. FindName is the documented trigger for
    /// x:Load; calling it again once the element exists simply returns it.
    /// </summary>
    private void Realize(string name)
    {
        try
        {
            _ = FindName(name);
        }
        catch (Exception error)
        {
            AppLog.Error($"Could not create the deferred element '{name}'", error);
        }
    }

    /// <summary>
    /// Drops the compiled bindings when the home screen is navigated away from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// x:Bind subscribes straight to the source's PropertyChanged and never
    /// unsubscribes - the generated code has no Unloaded handling at all. That is
    /// harmless when the source dies with the view, and a leak when it does not. Here
    /// it does not: PersonsViewModel is a singleton, so every binding on this page and
    /// on the two controls below it is a reference from an object that lives forever to
    /// one that should not. Without this, each lock and unlock would strand a whole page
    /// tree - the rail, the grid, the cards and both modals - in memory for the rest of
    /// the session.
    /// </para>
    /// <para>
    /// Safe because the home screen is never navigated back to. Its only exit is the
    /// lock button, which clears the back stack, and unlocking builds a fresh instance
    /// by way of the welcome screen. If a route back is ever added, this has to become
    /// conditional - bindings do not restart once stopped.
    /// </para>
    /// </remarks>
    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        base.OnNavigatedFrom(args);

        _persons.PropertyChanged -= OnPersonsPropertyChanged;
        _websites.PropertyChanged -= OnWebsitesPropertyChanged;
        _accounts.PropertyChanged -= OnAccountsPropertyChanged;
        _services.PropertyChanged -= OnServicesPropertyChanged;
        ViewModel.PropertyChanged -= OnSectionChanged;

        // Unsubscribe before stopping, so a transition caught in flight by the lock
        // button cannot run its completion against a page that is on its way out and
        // start the next one.
        _sectionOut.Completed -= OnSectionHidden;
        _sectionIn.Completed -= OnSectionShown;
        _sectionOut.Stop();
        _sectionIn.Stop();

        DirectoryView.ReleaseBindings();

        // Null when the rail never reached Websites and the idle warm-up had not run.
        SitesView?.ReleaseBindings();
        WebsiteModal?.ReleaseBindings();

        // Likewise for Accounts.
        AccountsGrid?.ReleaseBindings();
        AccountModal?.ReleaseBindings();

        // And Services.
        ServicesGrid?.ReleaseBindings();
        ServiceModal?.ReleaseBindings();

        // Null when the user never opened a person and the idle warm-up had not run.
        PersonModal?.ReleaseBindings();

        Bindings.StopTracking();
    }
}
