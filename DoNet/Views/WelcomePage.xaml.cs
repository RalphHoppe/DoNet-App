using System;
using System.Threading.Tasks;
using DoNet.Contracts;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Views;

/// <summary>
/// Shown after unlocking: "Welcome to DoNet" writes itself on, holds, and lifts off.
/// </summary>
/// <remarks>
/// Shown between unlocking and the home screen.
/// </remarks>
/// <remarks>
/// Deliberately shorter than the boot splash. That one plays once per launch; this one
/// plays on every unlock, so the stagger in <see cref="Controls.WelcomeWordmark"/> is
/// well under half the lockup's despite the phrase being nearly three times as long.
/// </remarks>
public sealed partial class WelcomePage : Page
{
    /// <summary>Beat between the phrase finishing and starting to rewind.</summary>
    private const int HoldMs = 380;

    /// <summary>
    /// Hard ceiling on the whole sequence. Whatever happens to the animation, the user
    /// reaches the home screen - being stranded on a blank page is the worst thing this
    /// screen could do.
    /// </summary>
    private const int WatchdogMs = 6000;

    private readonly INavigationService _navigation;
    private bool _started;

    public WelcomePage()
    {
        InitializeComponent();

        _navigation = App.Current.Services.GetRequiredService<INavigationService>();

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        // Loaded can fire again if the page is ever re-parented; this plays once.
        if (_started)
        {
            return;
        }

        _started = true;

        // Open the person store now, while the wordmark is drawing. This screen runs
        // for roughly 2.65 seconds of animation the user is already watching, which is
        // ample cover for an encrypted file open - so the directory has its first page
        // ready instead of showing a spinner the moment it appears. Deliberately not
        // awaited: the welcome animation must never wait on storage.
        _ = App.Current.Services.GetRequiredService<PersonsViewModel>().PreloadAsync();

        try
        {
            await Task.WhenAny(PlaySequenceAsync(), Task.Delay(WatchdogMs));
        }
        catch (Exception)
        {
            // An animation failure must never strand the user here - fall through
            // and navigate anyway.
        }

        // Clearing the back stack matters more here than on the splash: without it,
        // Back from the home screen would replay the welcome and land straight back.
        _navigation.NavigateTo(typeof(HomePage), clearBackStack: true);
    }

    private async Task PlaySequenceAsync()
    {
        await Wordmark.PlayIntroAsync();
        await Task.Delay(HoldMs);
        await Wordmark.PlayOutroAsync();
    }
}
