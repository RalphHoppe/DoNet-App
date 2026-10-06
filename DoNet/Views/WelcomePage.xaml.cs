using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Views;

/// <summary>
/// Shown after unlocking: "Welcome to DoNet" writes itself on, holds, and lifts off.
/// </summary>
/// <remarks>
/// Currently the last screen in the flow. It used to hand over to the home screen,
/// which has been removed pending a rebuild; until its replacement exists this page
/// simply holds after its animation rather than navigating somewhere that is gone.
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
    /// Hard ceiling on the whole sequence, so a stalled animation cannot hang the page.
    /// </summary>
    private const int WatchdogMs = 6000;

    private bool _started;

    public WelcomePage()
    {
        InitializeComponent();

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

        try
        {
            await Task.WhenAny(PlaySequenceAsync(), Task.Delay(WatchdogMs));
        }
        catch (Exception)
        {
            // An animation failure must never strand the user on a blank page; the
            // wordmark is left wherever it got to rather than the exception escaping.
        }

        // Nothing follows yet. When the home screen is rebuilt, navigate to it here
        // with clearBackStack: true - otherwise Back would replay this animation and
        // land straight back on it.
    }

    private async Task PlaySequenceAsync()
    {
        await Wordmark.PlayIntroAsync();
        await Task.Delay(HoldMs);
        await Wordmark.PlayOutroAsync();
    }
}
