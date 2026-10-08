using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace DoNet.Controls;

/// <summary>
/// Lays children out in a row, wrapping to the next line when they run out of width.
/// </summary>
/// <remarks>
/// WinUI ships no wrap panel. ItemsRepeater with a UniformGridLayout comes closest and
/// is wrong here: payment-method chips are as wide as their text, and a uniform grid
/// would pad "USDT" out to the width of "Recovery Password". Fifty lines of layout is
/// cheaper than the workarounds.
/// </remarks>
public sealed class WrapPanel : Panel
{
    public static readonly DependencyProperty ItemSpacingProperty = DependencyProperty.Register(
        nameof(ItemSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

    public static readonly DependencyProperty LineSpacingProperty = DependencyProperty.Register(
        nameof(LineSpacing), typeof(double), typeof(WrapPanel),
        new PropertyMetadata(8d, OnLayoutPropertyChanged));

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

    private static void OnLayoutPropertyChanged(
        DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((WrapPanel)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        // Unbounded width means "as wide as you like" - one line. That happens inside
        // a horizontal StackPanel or a ScrollViewer that does not constrain, and
        // wrapping against infinity would put everything on one line anyway.
        double limit = double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width;

        Size child = new(double.PositiveInfinity, double.PositiveInfinity);

        double lineWidth = 0;
        double lineHeight = 0;
        double widest = 0;
        double total = 0;

        foreach (UIElement element in Children)
        {
            if (element.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            element.Measure(child);
            Size size = element.DesiredSize;

            double needed = lineWidth == 0 ? size.Width : lineWidth + ItemSpacing + size.Width;

            if (needed > limit && lineWidth > 0)
            {
                // Does not fit: close this line and start another.
                widest = Math.Max(widest, lineWidth);
                total += lineHeight + LineSpacing;

                lineWidth = size.Width;
                lineHeight = size.Height;
            }
            else
            {
                lineWidth = needed;
                lineHeight = Math.Max(lineHeight, size.Height);
            }
        }

        widest = Math.Max(widest, lineWidth);
        total += lineHeight;

        return new Size(
            double.IsInfinity(availableSize.Width) ? widest : Math.Min(widest, availableSize.Width),
            total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        double y = 0;
        double lineHeight = 0;

        foreach (UIElement element in Children)
        {
            if (element.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            Size size = element.DesiredSize;
            double needed = x == 0 ? size.Width : x + ItemSpacing + size.Width;

            if (needed > finalSize.Width && x > 0)
            {
                x = 0;
                y += lineHeight + LineSpacing;
                lineHeight = 0;
            }
            else if (x > 0)
            {
                x += ItemSpacing;
            }

            element.Arrange(new Rect(x, y, size.Width, size.Height));

            x += size.Width;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return finalSize;
    }
}
