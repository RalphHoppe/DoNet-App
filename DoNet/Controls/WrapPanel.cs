using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DoNet.Controls;

/// <summary>
/// Lays children out in a row, wrapping to the next line when they run out of width,
/// and optionally capping the number of lines.
/// </summary>
/// <remarks>
/// <para>
/// WinUI ships no wrap panel. ItemsRepeater with a UniformGridLayout comes closest
/// and is wrong here: payment-method chips are as wide as their text, and a uniform
/// grid would pad "USDT" out to the width of "Recovery Password".
/// </para>
/// <para>
/// <see cref="MaxLines"/> is what keeps a card's height fixed no matter how many
/// methods a record has. The children that do not fit are arranged at zero size
/// rather than collapsed: WinUI clips a child arranged smaller than it asked for, so
/// a zero rectangle draws nothing and takes no hits, and - unlike setting Visibility
/// - it changes no property, so it cannot dirty layout and start the measure loop
/// over again.
/// </para>
/// </remarks>
public sealed class WrapPanel : Panel
{
    /// <summary>Identifies the <see cref="ItemSpacing"/> property.</summary>
    public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register(
        nameof(ItemSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="LineSpacing"/> property.</summary>
    public static readonly DependencyProperty LineSpacingProperty = DependencyProperty.Register(
        nameof(LineSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="MaxLines"/> property.</summary>
    public static readonly DependencyProperty MaxLinesProperty = DependencyProperty.Register(
        nameof(MaxLines), typeof(int), typeof(WrapPanel),
        new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="UseLastChildAsOverflow"/> property.</summary>
    public static readonly DependencyProperty UseLastChildAsOverflowProperty =
        DependencyProperty.Register(
            nameof(UseLastChildAsOverflow), typeof(bool), typeof(WrapPanel),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    /// <summary>Identifies the <see cref="HiddenCount"/> property.</summary>
    public static readonly DependencyProperty HiddenCountProperty = DependencyProperty.Register(
        nameof(HiddenCount), typeof(int), typeof(WrapPanel), new PropertyMetadata(0));

    private readonly List<Rect> _slots = [];
    private int _hidden;
    private double _lineEnd;
    private double _lineTop;

    /// <summary>Gap between items on the same line.</summary>
    public double ItemSpacing
    {
        get => (double)GetValue(ItemSpacingProperty);
        set => SetValue(ItemSpacingProperty, value);
    }

    /// <summary>Gap between lines.</summary>
    public double LineSpacing
    {
        get => (double)GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    /// <summary>Most lines to use, or zero for as many as the content needs.</summary>
    public int MaxLines
    {
        get => (int)GetValue(MaxLinesProperty);
        set => SetValue(MaxLinesProperty, value);
    }

    /// <summary>
    /// Treats the last child as a marker shown only when something did not fit.
    /// </summary>
    /// <remarks>
    /// Give that child a MinWidth. The panel has to reserve room for the marker
    /// before it knows how many items were dropped, so a marker whose width changes
    /// with its own text ("+1" against "+12") could change the answer it is
    /// reporting. A fixed floor makes the second pass agree with the first.
    /// </remarks>
    public bool UseLastChildAsOverflow
    {
        get => (bool)GetValue(UseLastChildAsOverflowProperty);
        set => SetValue(UseLastChildAsOverflowProperty, value);
    }

    /// <summary>How many items the last layout could not show.</summary>
    public int HiddenCount
    {
        get => (int)GetValue(HiddenCountProperty);
        private set => SetValue(HiddenCountProperty, value);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        Size unbounded = new(double.PositiveInfinity, double.PositiveInfinity);
        foreach (UIElement child in Children)
        {
            child.Measure(unbounded);
        }

        // An unbounded width means "as wide as you like", which is one line. That
        // happens inside a horizontal StackPanel, and wrapping against infinity
        // would put everything on one line anyway.
        double limit = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;
        Size used = BuildLayout(limit);

        if (HiddenCount != _hidden)
        {
            HiddenCount = _hidden;
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? used.Width : Math.Min(used.Width, availableSize.Width),
            used.Height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange can be given a different width than measure was offered, so the
        // slots are recomputed rather than trusted.
        BuildLayout(finalSize.Width);

        for (int i = 0; i < Children.Count; i++)
        {
            Children[i].Arrange(_slots[i]);
        }

        return finalSize;
    }

    private static void OnLayoutPropertyChanged(
        DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((WrapPanel)sender).InvalidateMeasure();

    /// <summary>
    /// Works out where every child goes, and returns the space that takes.
    /// </summary>
    private Size BuildLayout(double limit)
    {
        int count = Children.Count;
        UIElement? marker = UseLastChildAsOverflow && count > 0 ? Children[count - 1] : null;
        int itemCount = marker is null ? count : count - 1;

        _slots.Clear();
        for (int i = 0; i < count; i++)
        {
            _slots.Add(default);
        }

        // First pass keeps no room for the marker, because most records do fit and
        // reserving space they do not need would drop an item for no reason.
        Size size = Place(itemCount, limit, 0);

        if (marker is not null && _hidden > 0)
        {
            // Something was dropped, so the marker will be drawn and needs its space
            // back on the last line it is allowed to use.
            size = Place(itemCount, limit, marker.DesiredSize.Width + ItemSpacing);

            double x = _lineEnd == 0 ? 0 : _lineEnd + ItemSpacing;
            Size markerSize = marker.DesiredSize;
            _slots[count - 1] = new Rect(x, _lineTop, markerSize.Width, markerSize.Height);

            size = new Size(
                Math.Max(size.Width, x + markerSize.Width),
                Math.Max(size.Height, _lineTop + markerSize.Height));
        }

        return size;
    }

    /// <summary>
    /// Places the items, stopping when the allowed lines run out.
    /// </summary>
    /// <param name="itemCount">How many of the children are items rather than the marker.</param>
    /// <param name="limit">Width to wrap against.</param>
    /// <param name="reserve">Width to keep free at the end of the final line.</param>
    private Size Place(int itemCount, double limit, double reserve)
    {
        int maxLines = MaxLines <= 0 ? int.MaxValue : MaxLines;
        double x = 0;
        double y = 0;
        double lineHeight = 0;
        double widest = 0;
        int line = 1;
        int placed = 0;

        for (int i = 0; i < itemCount; i++)
        {
            Size size = Children[i].DesiredSize;
            double startX = x == 0 ? 0 : x + ItemSpacing;
            double endX = startX + size.Width;
            double needed = endX + (line == maxLines ? reserve : 0);

            if (needed > limit && x > 0)
            {
                if (line == maxLines)
                {
                    break;
                }

                line++;
                y += lineHeight + LineSpacing;
                x = 0;
                lineHeight = 0;
                startX = 0;
                endX = size.Width;
            }

            // An item wider than the whole line is placed anyway, on a line of its
            // own, rather than being dropped silently.
            _slots[i] = new Rect(startX, y, size.Width, size.Height);
            x = endX;
            lineHeight = Math.Max(lineHeight, size.Height);
            widest = Math.Max(widest, x);
            placed++;
        }

        for (int i = placed; i < itemCount; i++)
        {
            _slots[i] = default;
        }

        _hidden = itemCount - placed;
        _lineEnd = x;
        _lineTop = y;

        return new Size(widest, y + lineHeight);
    }
}
