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
    private readonly PersonsViewModel _persons;
    private bool _warmUpScheduled;

    public HomePage()
    {
        // Before InitializeComponent, not after: x:Bind resolves its root object while
        // the generated code runs, so a view model assigned afterwards would leave every
        // binding on this page pointing at null.
        ViewModel = App.Current.Services.GetRequiredService<HomeViewModel>();

        _persons = App.Current.Services.GetRequiredService<PersonsViewModel>();

        InitializeComponent();

        // The modals are deferred, so nothing is listening for the request to open one
        // until they exist. This page watches on their behalf and realises them.
        _persons.PropertyChanged += OnPersonsPropertyChanged;

        Loaded += OnLoaded;
    }

    public HomeViewModel ViewModel { get; }

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
                intro.Completed += (_, _) => QueueModalWarmUp();
            }

            intro.Begin();
        }
        else
        {
            QueueModalWarmUp();
        }
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
                Realize("ConfirmModal");
            });

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

        DirectoryView.ReleaseBindings();

        // Null when the user never opened a person and the idle warm-up had not run.
        PersonModal?.ReleaseBindings();

        Bindings.StopTracking();
    }
}
