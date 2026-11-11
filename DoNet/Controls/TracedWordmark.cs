using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace DoNet.Controls;

/// <summary>
/// Timing for a draw-on wordmark, in milliseconds.
/// </summary>
/// <remarks>
/// A longer phrase needs a shorter stagger or the whole thing outstays its welcome:
/// the lockup is five letters, "Welcome to DoNet" is fourteen, and at the same stagger
/// the second would take nearly twice as long to write itself.
/// </remarks>
internal sealed record TraceTiming
{
    public int LetterStartMs { get; init; } = 250;
    public int LetterStaggerMs { get; init; } = 140;
    public int LetterDrawMs { get; init; } = 600;
    public int FillDelayMs { get; init; } = 430;
    public int FillFadeMs { get; init; } = 260;

    public int OutroStaggerMs { get; init; } = 80;
    public int OutroFillFadeMs { get; init; } = 200;
    public int OutroTraceDelayMs { get; init; } = 110;
    public int OutroTraceMs { get; init; } = 380;

    public int RingDrawMs { get; init; } = 620;
    public int OutroRingDelayMs { get; init; } = 240;
    public int OutroRingMs { get; init; } = 440;
}

/// <summary>
/// Drives the draw-on effect for a set of letter outlines.
/// </summary>
/// <remarks>
/// <para>
/// Each letter exists twice: a hairline "trace" Path and a solid "fill" Path sitting
/// exactly on top of it. The draw-on effect animates <see cref="Shape.StrokeDashOffset"/>
/// of the trace from "one full path length" down to zero, which slides a single dash the
/// length of the whole outline across it - the standard SVG line-draw trick. Once a
/// letter has finished tracing, its fill cross-fades in over the hairline.
/// </para>
/// <para>
/// The dash length each Path needs is baked into its <see cref="Shape.StrokeDashArray"/>
/// by the generator scripts, because XAML measures dashes in multiples of StrokeThickness
/// and the value cannot be recovered here without re-measuring the outline. Reading it
/// back off the Path keeps the number in exactly one place.
/// </para>
/// <para>
/// This is a plain class rather than a base control: both wordmarks are generated
/// UserControls, and sharing through inheritance would mean the XAML root element had to
/// be the base type, which the generators would then have to know about.
/// </para>
/// </remarks>
internal sealed class TracedWordmark
{
    private readonly IReadOnlyList<Path> _traces;
    private readonly IReadOnlyList<Path> _fills;
    private readonly Path? _ring;

    private Storyboard? _running;
    private TaskCompletionSource? _runningCompletion;

    /// <param name="traces">Letter outlines, in writing order.</param>
    /// <param name="fills">Solid letters, in the same order.</param>
    /// <param name="ring">Optional mark drawn before the first letter.</param>
    public TracedWordmark(
        IReadOnlyList<Path> traces,
        IReadOnlyList<Path> fills,
        Path? ring = null,
        TraceTiming? timing = null)
    {
        _traces = traces;
        _fills = fills;
        _ring = ring;
        Timing = timing ?? new TraceTiming();
    }

    public TraceTiming Timing { get; }

    /// <summary>Hides the fills and rewinds every trace to "not yet drawn".</summary>
    public void ApplyIntroStartState()
    {
        Stop();

        if (_ring is not null)
        {
            _ring.StrokeDashOffset = DashLength(_ring);
        }

        for (var i = 0; i < _traces.Count; i++)
        {
            _traces[i].Opacity = 1;
            _traces[i].StrokeDashOffset = DashLength(_traces[i]);
            _fills[i].Opacity = 0;
        }
    }

    /// <summary>Puts the wordmark in its final, fully drawn state with no animation.</summary>
    public void ApplyCompleteState()
    {
        Stop();

        if (_ring is not null)
        {
            _ring.StrokeDashOffset = 0;
        }

        for (var i = 0; i < _traces.Count; i++)
        {
            _traces[i].Opacity = 0;
            _traces[i].StrokeDashOffset = 0;
            _fills[i].Opacity = 1;
        }
    }

    /// <summary>Draws the mark, then writes the letters one at a time.</summary>
    /// <remarks>
    /// The end state is written back as local values once the storyboard finishes.
    /// A completed Storyboard only *holds* its final values; calling Stop on it - which
    /// the next animation has to do - snaps every property back to what it was before,
    /// so without this the wordmark would blink out between the intro and the outro.
    /// </remarks>
    public async Task PlayIntroAsync()
    {
        ApplyIntroStartState();
        await RunAsync(BuildIntro());
        ApplyCompleteState();
    }

    /// <summary>Un-draws the letters from the last back to the first, then the mark.</summary>
    public async Task PlayOutroAsync()
    {
        await RunAsync(BuildOutro());
        ApplyIntroStartState();
    }

    public void Stop()
    {
        _running?.Stop();
        _running = null;

        // Release anyone awaiting the storyboard. Without this, stopping early - the
        // control being unloaded mid-animation, say - would leave the caller hanging
        // on a task that can never complete.
        _runningCompletion?.TrySetResult();
        _runningCompletion = null;
    }

    private Storyboard BuildIntro()
    {
        var board = new Storyboard();

        if (_ring is not null)
        {
            board.Children.Add(Animate(_ring, nameof(Shape.StrokeDashOffset),
                DashLength(_ring), 0, beginMs: 0, durationMs: Timing.RingDrawMs,
                ease: new CubicEase { EasingMode = EasingMode.EaseInOut }, dependent: true));
        }

        for (var i = 0; i < _traces.Count; i++)
        {
            var begin = Timing.LetterStartMs + (i * Timing.LetterStaggerMs);

            board.Children.Add(Animate(_traces[i], nameof(Shape.StrokeDashOffset),
                DashLength(_traces[i]), 0, begin, Timing.LetterDrawMs,
                ease: new CubicEase { EasingMode = EasingMode.EaseInOut }, dependent: true));

            // the solid letter rises over its own outline, then the outline is dropped
            board.Children.Add(Animate(_fills[i], nameof(UIElement.Opacity),
                0, 1, begin + Timing.FillDelayMs, Timing.FillFadeMs));

            board.Children.Add(Animate(_traces[i], nameof(UIElement.Opacity),
                1, 0, begin + Timing.FillDelayMs + Timing.FillFadeMs, 1));
        }

        return board;
    }

    private Storyboard BuildOutro()
    {
        var board = new Storyboard();
        var count = _traces.Count;

        for (var i = 0; i < count; i++)
        {
            // reverse order: the last letter lifts first
            var trace = _traces[count - 1 - i];
            var fill = _fills[count - 1 - i];
            var begin = i * Timing.OutroStaggerMs;

            // bring the hairline back so there is something to rewind
            board.Children.Add(Animate(trace, nameof(UIElement.Opacity), 0, 1, begin, 1));
            board.Children.Add(Animate(fill, nameof(UIElement.Opacity), 1, 0, begin, Timing.OutroFillFadeMs));

            // offset 0 -> +length erases from the end of the stroke backwards,
            // which reads as the pen being lifted back along the letter
            board.Children.Add(Animate(trace, nameof(Shape.StrokeDashOffset),
                0, DashLength(trace), begin + Timing.OutroTraceDelayMs, Timing.OutroTraceMs,
                ease: new CubicEase { EasingMode = EasingMode.EaseIn }, dependent: true));
        }

        if (_ring is not null)
        {
            board.Children.Add(Animate(_ring, nameof(Shape.StrokeDashOffset),
                0, DashLength(_ring), Timing.OutroRingDelayMs, Timing.OutroRingMs,
                ease: new CubicEase { EasingMode = EasingMode.EaseIn }, dependent: true));
        }

        return board;
    }

    private Task RunAsync(Storyboard board)
    {
        Stop();

        var completion = new TaskCompletionSource();

        _running = board;
        _runningCompletion = completion;

        board.Completed += (_, _) => completion.TrySetResult();
        board.Begin();

        return completion.Task;
    }

    /// <summary>
    /// The dash length the generator baked into the Path, in StrokeThickness units -
    /// which is exactly the offset that hides the whole outline.
    /// </summary>
    private static double DashLength(Shape shape) =>
        shape.StrokeDashArray is { Count: > 0 } dashes ? dashes[0] : 0;

    private static DoubleAnimation Animate(
        DependencyObject target,
        string property,
        double from,
        double to,
        int beginMs,
        int durationMs,
        EasingFunctionBase? ease = null,
        bool dependent = false)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
            EasingFunction = ease,

            // StrokeDashOffset cannot be handed to the compositor, so WinUI drops the
            // animation on the floor unless it is explicitly allowed to run on the UI thread.
            EnableDependentAnimation = dependent,
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);

        return animation;
    }
}
