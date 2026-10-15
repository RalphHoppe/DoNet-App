using System;
using DoNet.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Controls;

/// <summary>Which of the directory's four conditions to draw.</summary>
public enum StateArtKind
{
    /// <summary>The store is being opened.</summary>
    Loading,

    /// <summary>The store opened and holds nothing.</summary>
    Empty,

    /// <summary>The store holds records, but none match the search.</summary>
    NoMatch,

    /// <summary>The store could not be opened.</summary>
    Error,
}

/// <summary>
/// The illustration shown with each directory state message.
/// </summary>
/// <remarks>
/// <para>
/// One control, four pictures, and only the requested one is ever built: each
/// illustration is <c>x:Load="False"</c> and realised through <c>FindName</c> when
/// <see cref="Kind"/> is applied. Four of these sit on the Persons screen at once -
/// one per state - so building all four drawings in all four instances would be
/// sixteen pictures to show one.
/// </para>
/// <para>
/// <see cref="IsActive"/> is separate from visibility on purpose. A collapsed element
/// still runs its storyboards, so binding only <c>Visibility</c> would leave three
/// unseen animations looping forever, waking the compositor sixty times a second to
/// draw nothing. The host binds the same view-model flag to both.
/// </para>
/// </remarks>
public sealed partial class StateArt : UserControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(StateArtKind),
        typeof(StateArt),
        new PropertyMetadata(StateArtKind.Loading, OnStateChanged));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(StateArt),
        new PropertyMetadata(false, OnStateChanged));

    private Storyboard? _playing;
    private bool _ready;

    public StateArt()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            // Deferred children cannot be realised, and storyboards cannot resolve
            // their targets, until the control is in the tree.
            _ready = true;
            Refresh();
        };

        Unloaded += (_, _) =>
        {
            _ready = false;
            Stop();
        };
    }

    /// <summary>Which illustration this instance draws. Fixed per usage.</summary>
    public StateArtKind Kind
    {
        get => (StateArtKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Whether the state is the one currently on screen.</summary>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((StateArt)sender).Refresh();

    private void Refresh()
    {
        if (!_ready)
        {
            return;
        }

        Stop();

        if (!IsActive)
        {
            return;
        }

        string name = Kind switch
        {
            StateArtKind.Empty => "EmptyArt",
            StateArtKind.NoMatch => "NoMatchArt",
            StateArtKind.Error => "ErrorArt",
            _ => "LoadingArt",
        };

        try
        {
            // FindName is the documented trigger for x:Load; once the element exists
            // it simply returns it, so repeated activation costs nothing.
            if (FindName(name) is not FrameworkElement art)
            {
                return;
            }

            art.Visibility = Visibility.Visible;

            // The storyboard lives in the illustration's own Resources rather than the
            // control's, so it is realised together with the elements it targets and
            // can always resolve them.
            if (art.Resources.ContainsKey("Play")
                && art.Resources["Play"] is Storyboard board)
            {
                _playing = board;
                board.Begin();
            }
        }
        catch (Exception error)
        {
            // An illustration is decoration. Losing it must never take the state
            // message - which is the part that actually tells the user something -
            // down with it.
            AppLog.Error($"Could not show the '{Kind}' state illustration", error);
        }
    }

    private void Stop()
    {
        if (_playing is null)
        {
            return;
        }

        try
        {
            // Stop rather than Pause: a stopped storyboard snaps its targets back to
            // their resting values, so a state that comes back does not resume
            // mid-shake or half-faded.
            _playing.Stop();
        }
        catch (Exception error)
        {
            AppLog.Error("Could not stop a state illustration", error);
        }

        _playing = null;
    }
}
