using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace EasyNet.Controls;

/// <summary>How the mark behaves once it is on screen.</summary>
public enum MarkMode
{
    /// <summary>The settled logo: everything in its final place, nothing moves.</summary>
    Static,

    /// <summary>The splash: the entrance plays once, then the loop takes over.</summary>
    Splash,

    /// <summary>No entrance, just the loop - for waits that begin mid-motion.</summary>
    Loop,
}

/// <summary>
/// The EasyNet mark, animated. See the XAML for the geometry; this is the
/// SVG stylesheet's choreography, rebuilt natively.
/// </summary>
/// <remarks>
/// <para>
/// The design's own SVGs describe the motion exactly - which parts slide in
/// from where, how long the ring takes to appear, when each node pops, what
/// loops and at what phase - and this code follows those numbers rather than
/// approximating them by eye. The numbers themselves live in
/// EasyNetMark.geometry.cs, generated from the SVGs.
/// </para>
/// <para>
/// Two deliberate departures, both forced by what XAML can animate: the SVG
/// reveals its ring through a mask that sweeps the ellipse, so the ring's
/// six segments arrive in a staggered cascade instead - the same window,
/// the same ease, the same arrival-before-the-nodes order, read as a sweep;
/// and the SVG's travelling light is a dash riding the whole orbit behind
/// that mask, so the light here is a pair of shine waves walking the
/// segments themselves - a broad amber one and a brief cream one, half a
/// cycle apart, visiting the segments in the orbit's own order.
///</para>
/// <para>
/// Everything runs on the composition-friendly properties - transforms and
/// opacity - so the loops cost no layout and no CPU beyond the ticking
/// clock. When the system says animations are off, none of this runs: the
/// settled logo the XAML declares is what shows.
///</para>
///</remarks>
public sealed partial class EasyNetMark : UserControl
{
    /// <summary>Identifies the <see cref="Mode"/> property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(MarkMode), typeof(EasyNetMark),
        new PropertyMetadata(MarkMode.Static));

    private Storyboard? _entrance;
    private bool _played;

    public EasyNetMark()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    /// <summary>How the mark behaves once it is on screen.</summary>
    public MarkMode Mode
    {
        get => (MarkMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;

        if (_played || !AnimationsEnabled())
        {
            return;
        }

        _played = true;

        switch (Mode)
        {
            case MarkMode.Splash:
                _entrance = BuildEntrance();
                _entrance.Completed += (_, _) => BeginLoop();
                _entrance.Begin();
                break;

            case MarkMode.Loop:
                BeginLoop();
                break;
        }
    }

    /// <summary>
    /// The loop, begun directly or on the entrance's completion - the SVG's
    /// --ld, expressed as "start when the arrival is done".
    /// </summary>
    private void BeginLoop()
    {
        if (Mode == MarkMode.Static)
        {
            return;
        }

        // A mark that never played an entrance has not brought the halo up;
        // the loop's breathing assumes it is already there.
        if (Mode == MarkMode.Loop)
        {
            Halo.Opacity = 1;
        }

        BuildLoop().Begin();
    }

    /// <summary>Whether the system allows animation at all.</summary>
    private static bool AnimationsEnabled() => new UISettings().AnimationsEnabled;

    // ------------------------------------------------------------------
    // The entrance
    // ------------------------------------------------------------------

    /// <summary>
    /// The splash's arrival: the slabs slide in along their own diagonals,
    /// the bars arrive from the right, the ring cascades into place, the halo
    /// warms up behind it, and the nodes pop onto the ring's gaps - each on
    /// the design's own delay.
    /// </summary>
    private Storyboard BuildEntrance()
    {
        Storyboard board = new();

        // The starting states. With every animation carrying an explicit From,
        // these matter only for the moments before each one's begin time - but
        // those moments are exactly what "not yet arrived" looks like.
        foreach (UIElement part in new UIElement[]
                 {
                     Slab1, Slab2, Bar1, Bar2, Halo,
                     RingSeg1, RingSeg2, RingSeg3, RingSeg4, RingSeg5, RingSeg6,
                     Node1, Node2, Node3, Node4,
                 })
        {
            part.Opacity = 0;
        }

        // The slabs' diagonals and the bars' approach, from the stylesheet's
        // own --x/--y values: (-110,-70), (110,70), and +170 for both bars.
        SlideIn(board, Slab1, Slab1Shift, fromX: -110, fromY: -70, delay: 0.00);
        SlideIn(board, Slab2, Slab2Shift, fromX: 110, fromY: 70, delay: 0.12);
        SlideIn(board, Bar1, Bar1Drift, fromX: 170, fromY: 0, delay: 0.40);
        SlideIn(board, Bar2, Bar2Drift, fromX: 170, fromY: 0, delay: 0.52);

        // The ring: the whole orbit settles from a breath smaller while its
        // six segments cascade in - a half-second window starting at the
        // stylesheet's own 0.6s mark, which is when its mask began to sweep.
        RingGrow.ScaleX = 0.965;
        RingGrow.ScaleY = 0.965;
        Add(board, Anim(0.965, 1, Seconds(0.9), QuintOut()), RingGrow, "ScaleX", delay: 0.6);
        Add(board, Anim(0.965, 1, Seconds(0.9), QuintOut()), RingGrow, "ScaleY", delay: 0.6);

        for (int i = 0; i < SegmentPhases.Length; i++)
        {
            Add(board, Anim(0, 1, Seconds(0.45), QuintOut()), RingSegment(i), "Opacity",
                delay: 0.60 + (0.12 * i));
        }

        // The halo warms up behind the whole arrival.
        Add(board, Anim(0, 1, Seconds(1.4), new CubicEase { EasingMode = EasingMode.EaseOut }),
            Halo, "Opacity", delay: 0.6);

        // The nodes pop onto the ring's gaps, each on its own delay.
        for (int i = 0; i < NodePhases.Length; i++)
        {
            PopIn(board, i, NodePopDelays[i]);
        }

        return board;
    }

    /// <summary>Slides one part in from an offset while it fades up.</summary>
    private static void SlideIn(
        Storyboard board,
        UIElement element,
        TranslateTransform shift,
        double fromX,
        double fromY,
        double delay)
    {
        shift.X = fromX;
        shift.Y = fromY;

        Duration length = Seconds(0.9);

        Add(board, Anim(fromX, 0, length, QuintOut()), shift, "X", delay);
        Add(board, Anim(fromY, 0, length, QuintOut()), shift, "Y", delay);
        Add(board, Anim(0, 1, length, QuintOut()), element, "Opacity", delay);
    }

    /// <summary>Pops a node in: scale from nothing with a gentle overshoot.</summary>
    private void PopIn(Storyboard board, int index, double delay)
    {
        UIElement node = NodeElement(index);
        ScaleTransform grow = NodeGrow(index);

        grow.ScaleX = 0;
        grow.ScaleY = 0;

        Duration length = Seconds(0.7);

        // The design's pop carries a slight overshoot (cubic-bezier .34,1.56,
        // .64,1); BackEase at a modest amplitude is its native counterpart.
        var pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };

        Add(board, Anim(0, 1, length, QuintOut()), node, "Opacity", delay);
        Add(board, Anim(0, 1, length, pop), grow, "ScaleX", delay);
        Add(board, Anim(0, 1, length, pop), grow, "ScaleY", delay);
    }

    // ------------------------------------------------------------------
    // The loop
    // ------------------------------------------------------------------

    /// <summary>
    /// The living logo: two waves of light walk the ring's segments - a broad
    /// amber one and a brief cream one, half a cycle apart - while the nodes
    /// pulse in their own phases, the bars drift a breath to the right and
    /// back, and the halo breathes, all on the stylesheet's period.
    /// </summary>
    private Storyboard BuildLoop()
    {
        Storyboard board = new();

        for (int i = 0; i < SegmentPhases.Length; i++)
        {
            double phase = SegmentPhases[i] * LoopPeriodSeconds;

            // The amber wave: broad and soft, like the design's long arc.
            ShineWave(board, ShineA(i), LoopPeriodSeconds, phase,
                peak: 0.50, rise: 0.10, fall: 0.55);

            // The cream wave: a brief glint, half a cycle behind the amber.
            ShineWave(board, ShineB(i), LoopPeriodSeconds, phase + (LoopPeriodSeconds / 2),
                peak: 0.35, rise: 0.05, fall: 0.25);
        }

        // The nodes' pulses: up to 1.35 by the period's own 5%, settled again
        // by 18%, each offset by its phase so the ring never pulses as one.
        for (int i = 0; i < NodePhases.Length; i++)
        {
            double begin = (NodePhases[i] * LoopPeriodSeconds) - 0.1;

            Add(board, Pulse(), NodeGrow(i), "ScaleX", begin);
            Add(board, Pulse(), NodeGrow(i), "ScaleY", begin);
        }

        // The bars' drift: ten art-units right and home again, the second bar
        // a third of a cycle behind the first. The entrance used these same
        // transforms to slide the bars in; a begun animation outranks a held
        // one, so the loop simply takes over the property.
        Drift(board, Bar1Drift, delay: 0.00);
        Drift(board, Bar2Drift, delay: 0.35);

        // The halo's breathing: a third again slower than everything else.
        Breathe(board, delay: 0);

        return board;
    }

    /// <summary>
    /// One wave of light over one segment: nothing, up to its peak, and away -
    /// repeated every period, so the wave walks the ring segment by segment.
    /// </summary>
    /// <remarks>
    /// The rise and fall are clipped to the period's end rather than allowed
    /// to spill past it; a segment near the cycle's seam gets a shorter fall
    /// rather than a broken one.
    /// </remarks>
    private static void ShineWave(
        Storyboard board,
        UIElement shine,
        double periodSeconds,
        double phase,
        double peak,
        double rise,
        double fall)
    {
        double start = phase % periodSeconds;

        if (start + rise >= periodSeconds)
        {
            // The wave's moment fell exactly on the seam; it simply does not
            // show this cycle rather than showing half of one.
            return;
        }

        fall = Math.Min(fall, periodSeconds - start - rise);

        var wave = new DoubleAnimationUsingKeyFrames
        {
            Duration = Seconds(periodSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };

        wave.KeyFrames.Add(Linear(start, 0));
        wave.KeyFrames.Add(Spline(start + rise, peak, 0.16, 1.0, 0.3, 1.0));
        wave.KeyFrames.Add(Spline(start + rise + fall, 0, 0.3, 0.0, 0.4, 1.0));
        wave.KeyFrames.Add(Linear(periodSeconds, 0));

        Add(board, wave, shine, "Opacity", delay: 0);
    }

    /// <summary>One node's pulse, as scale keyframes over one whole period.</summary>
    private DoubleAnimationUsingKeyFrames Pulse()
    {
        var pulse = new DoubleAnimationUsingKeyFrames
        {
            Duration = Seconds(LoopPeriodSeconds),
            RepeatBehavior = RepeatBehavior.Forever,
        };

        // 5% of the period up, 18% of it settled: the stylesheet's own marks.
        pulse.KeyFrames.Add(Spline(0.00, 1.00, 0.16, 1.0, 0.3, 1.0));
        pulse.KeyFrames.Add(Spline(0.14, 1.35, 0.16, 1.0, 0.3, 1.0));
        pulse.KeyFrames.Add(Spline(0.50, 1.00, 0.3, 0.0, 0.4, 1.0));
        pulse.KeyFrames.Add(Spline(LoopPeriodSeconds, 1.00, 0.0, 0.0, 1.0, 1.0));

        return pulse;
    }

    /// <summary>One bar's drift: out, back, forever.</summary>
    private static void Drift(Storyboard board, TranslateTransform drift, double delay)
    {
        var sway = new DoubleAnimationUsingKeyFrames
        {
            Duration = Seconds(2.6),
            RepeatBehavior = RepeatBehavior.Forever,
        };

        // The stylesheet's own cubic-bezier(.65,0,.35,1) on each half.
        sway.KeyFrames.Add(Spline(0.0, 0.0, 0.65, 0.0, 0.35, 1.0));
        sway.KeyFrames.Add(Spline(1.3, 10.0, 0.65, 0.0, 0.35, 1.0));
        sway.KeyFrames.Add(Spline(2.6, 0.0, 0.65, 0.0, 0.35, 1.0));

        Add(board, sway, drift, "X", delay);
    }

    /// <summary>The halo's slow dim and return.</summary>
    private void Breathe(Storyboard board, double delay)
    {
        var breathe = new DoubleAnimationUsingKeyFrames
        {
            Duration = Seconds(3.6),
            RepeatBehavior = RepeatBehavior.Forever,
        };

        breathe.KeyFrames.Add(Spline(0.0, 1.0, 0.45, 0.0, 0.55, 1.0));
        breathe.KeyFrames.Add(Spline(1.8, 0.5, 0.45, 0.0, 0.55, 1.0));
        breathe.KeyFrames.Add(Spline(3.6, 1.0, 0.45, 0.0, 0.55, 1.0));

        Add(board, breathe, Halo, "Opacity", delay);
    }

    // ------------------------------------------------------------------
    // The small parts animations are made of
    // ------------------------------------------------------------------

    private static Duration Seconds(double value) => TimeSpan.FromSeconds(value);

    /// <summary>The stylesheet's arrival ease: cubic-bezier(.16,1,.3,1).</summary>
    private static EasingFunctionBase QuintOut() => new QuinticEase { EasingMode = EasingMode.EaseOut };

    private static DoubleAnimation Anim(
        double from, double to, Duration duration, EasingFunctionBase? ease) =>
        new()
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = ease,
        };

    /// <summary>A keyframe with the design's own spline, points given plainly.</summary>
    private static SplineDoubleKeyFrame Spline(
        double keySeconds, double value,
        double cp1x, double cp1y, double cp2x, double cp2y) =>
        new()
        {
            KeyTime = TimeSpan.FromSeconds(keySeconds),
            Value = value,
            KeySpline = new KeySpline
            {
                ControlPoint1 = new Windows.Foundation.Point(cp1x, cp1y),
                ControlPoint2 = new Windows.Foundation.Point(cp2x, cp2y),
            },
        };

    /// <summary>A hold-until keyframe: the wave's quiet before and after.</summary>
    private static LinearDoubleKeyFrame Linear(double keySeconds, double value) =>
        new()
        {
            KeyTime = TimeSpan.FromSeconds(keySeconds),
            Value = value,
        };

    private static void Add(
        Storyboard board, Timeline animation, DependencyObject target, string property, double delay)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        animation.BeginTime = TimeSpan.FromSeconds(delay);
        board.Children.Add(animation);
    }

    private UIElement RingSegment(int index) => index switch
    {
        0 => RingSeg1,
        1 => RingSeg2,
        2 => RingSeg3,
        3 => RingSeg4,
        4 => RingSeg5,
        _ => RingSeg6,
    };

    private UIElement ShineA(int index) => index switch
    {
        0 => ShineA1,
        1 => ShineA2,
        2 => ShineA3,
        3 => ShineA4,
        4 => ShineA5,
        _ => ShineA6,
    };

    private UIElement ShineB(int index) => index switch
    {
        0 => ShineB1,
        1 => ShineB2,
        2 => ShineB3,
        3 => ShineB4,
        4 => ShineB5,
        _ => ShineB6,
    };

    private UIElement NodeElement(int index) => index switch
    {
        0 => Node1,
        1 => Node2,
        2 => Node3,
        _ => Node4,
    };

    private ScaleTransform NodeGrow(int index) => index switch
    {
        0 => Node1Grow,
        1 => Node2Grow,
        2 => Node3Grow,
        _ => Node4Grow,
    };
}
