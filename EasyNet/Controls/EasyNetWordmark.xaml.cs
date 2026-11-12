using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;

namespace EasyNet.Controls;

/// <summary>Whether the wordmark plays its arrival.</summary>
public enum WordmarkMode
{
    /// <summary>The settled word: all seven letters in place, nothing moves.</summary>
    Static,

    /// <summary>The splash: the letters rise in, one sixtieth of a second apart.</summary>
    Splash,
}

/// <summary>
/// The EasyNet wordmark, drawn rather than typeset: Easy in ink, Net in the
/// brand gradient. See the XAML for the letterforms; this is their entrance.
/// </summary>
/// <remarks>
/// The design's stylesheet rises each letter 32 art-units from below on the
/// arrival ease, the first at 1.15s and each next 0.06s after - the mark is
/// still assembling when the word begins, and finishes just as the loop
/// takes over. Those numbers live in EasyNetWordmark.geometry.cs, generated
/// from the SVGs; this code just follows them.
///</remarks>
public sealed partial class EasyNetWordmark : UserControl
{
    /// <summary>Identifies the <see cref="Mode"/> property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(WordmarkMode), typeof(EasyNetWordmark),
        new PropertyMetadata(WordmarkMode.Static));

    public EasyNetWordmark()
    {
        InitializeComponent();

        Loaded += OnLoaded;
    }

    /// <summary>Whether the wordmark plays its arrival.</summary>
    public WordmarkMode Mode
    {
        get => (WordmarkMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Loaded -= OnLoaded;

        if (Mode != WordmarkMode.Splash || !new UISettings().AnimationsEnabled)
        {
            return;
        }

        Storyboard board = new();

        var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
        Duration length = TimeSpan.FromSeconds(0.7);

        for (int i = 0; i < LetterCount; i++)
        {
            UIElement letter = Letter(i);
            TranslateTransform rise = LetterRise(i);

            // Not yet risen: below its place and unseen, until its moment.
            letter.Opacity = 0;
            rise.Y = LetterRiseUnits;

            double delay = FirstLetterDelaySeconds + (LetterStaggerSeconds * i);

            Add(board, Anim(LetterRiseUnits, 0, length, ease), rise, "Y", delay);
            Add(board, Anim(0, 1, length, ease), letter, "Opacity", delay);
        }

        board.Begin();
    }

    private static DoubleAnimation Anim(
        double from, double to, Duration duration, EasingFunctionBase ease) =>
        new()
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = ease,
        };

    private static void Add(
        Storyboard board, Timeline animation, DependencyObject target, string property, double delay)
    {
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        animation.BeginTime = TimeSpan.FromSeconds(delay);
        board.Children.Add(animation);
    }

    private UIElement Letter(int index) => index switch
    {
        0 => Letter1,
        1 => Letter2,
        2 => Letter3,
        3 => Letter4,
        4 => Letter5,
        5 => Letter6,
        _ => Letter7,
    };

    private TranslateTransform LetterRise(int index) => index switch
    {
        0 => Letter1Rise,
        1 => Letter2Rise,
        2 => Letter3Rise,
        3 => Letter4Rise,
        4 => Letter5Rise,
        5 => Letter6Rise,
        _ => Letter7Rise,
    };
}
