using System;
using System.Runtime.InteropServices;
using EasyNet.Services;
using EasyNet.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace EasyNet;

/// <summary>
/// The shell window: a chrome-free white frame that hosts every page.
/// </summary>
/// <remarks>
/// The window opens at the design's own artboard size - the mockups are
/// 1400x800 - and never runs edge to edge on a display that cannot hold that,
/// because a window exactly the size of the work area reads as a botched
/// maximise rather than a deliberate default.
///</remarks>
public sealed partial class MainWindow : Window
{
    // The design's window: a comfortable desktop opening - the small thing
    // on screen is the card each entry screen centres, not the window, so
    // the window simply gives that card room.
    private const int DesignWidth = 800;
    private const int DesignHeight = 540;

    // ...but never edge to edge.
    private const double MaxWorkAreaFraction = 0.92;

    // The smallest window the card still breathes in: its narrow posture
    // plus the page's breathing margin either side.
    private const int MinimumWidth = 740;
    private const int MinimumHeight = 480;

    // Resize-border metrics, for keeping the window's grab edges out of the caption.
    private const int SmCxSizeFrame = 32;
    private const int SmCxPaddedBorder = 92;

    private readonly IntPtr _handle;

    /// <summary>Last known scale factor, to spot the window moving to another monitor.</summary>
    private double _lastScale;

    public MainWindow()
    {
        InitializeComponent();

        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ConfigureTitleBar();
        ApplyMinimumSize();
        SizeAndCentre(DesignWidth, DesignHeight);

        // The XamlRoot does not exist until the tree goes live, so the scale
        // watch cannot be attached from here.
        RootFrame.Loaded += OnRootFrameLoaded;

        // The caption region has to be re-declared whenever its pixel geometry
        // moves: on first layout, on every resize, and on a DPI change.
        DragRegion.Loaded += (_, _) => UpdateCaptionRegions();
        DragRegion.SizeChanged += (_, _) => UpdateCaptionRegions();

        INavigationService navigation = App.Current.Services.GetRequiredService<INavigationService>();
        navigation.Frame = RootFrame;
        navigation.NavigateTo(typeof(SplashPage));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    /// <summary>
    /// Removes the system title bar outright, leaving the close button in
    /// MainWindow.xaml as the only window chrome.
    /// </summary>
    /// <remarks>
    /// The border is kept, so the window still has its resize edges and drop
    /// shadow; only the caption goes.
    /// </remarks>
    private void ConfigureTitleBar()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }
    }

    /// <summary>
    /// Stops the window being resized smaller than the layout can take.
    /// </summary>
    /// <remarks>
    /// The limits are given in physical pixels and the presenter does not
    /// scale them, so they are multiplied by the monitor's DPI here - on a
    /// 150% display an unscaled 980 would really mean 653 and let the window
    /// shrink past the point the content fits.
    /// </remarks>
    private void ApplyMinimumSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        var dpi = GetDpiForWindow(_handle);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        presenter.PreferredMinimumWidth = (int)Math.Round(MinimumWidth * scale);
        presenter.PreferredMinimumHeight = (int)Math.Round(MinimumHeight * scale);

        _lastScale = scale;
    }

    private void OnRootFrameLoaded(object sender, RoutedEventArgs args)
    {
        RootFrame.Loaded -= OnRootFrameLoaded;

        if (RootFrame.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed += OnXamlRootChanged;
        }
    }

    /// <summary>
    /// Recalculates the minimum size when the window moves to a monitor with a
    /// different scale factor.
    /// </summary>
    /// <remarks>
    /// The presenter keeps the pixel values it was given across a DPI change,
    /// which silently loosens or tightens the limit by the ratio between the
    /// two monitors. This fires for ordinary size changes too, hence the
    /// scale comparison.
    /// </remarks>
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (Math.Abs(sender.RasterizationScale - _lastScale) > 0.001)
        {
            ApplyMinimumSize();
            UpdateCaptionRegions();
        }
    }

    /// <summary>
    /// Tells the window manager that the top strip is the caption, and that
    /// the close button inside it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declaring the region hands drag, double-click maximise, right-click
    /// system menu, edge snapping and multi-monitor handoff back to the window
    /// manager, which is the only thing that implements them correctly.
    /// </para>
    /// <para>
    /// Rects are in physical pixels relative to the client area, hence the
    /// RasterizationScale. Passthrough wins over Caption where the two
    /// overlap, which is what keeps the close button clickable and its hover
    /// state alive.
    /// </para>
    /// </remarks>
    private void UpdateCaptionRegions()
    {
        if (DragRegion.XamlRoot is not { } root || DragRegion.ActualWidth <= 0)
        {
            return;
        }

        var scale = root.RasterizationScale;
        InputNonClientPointerSource source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);

        // Hold the caption off the window's own resize edges. A caption region
        // claims every pixel it covers, so running it to x=0 and y=0 would take
        // the top-left grab handles with it and the window could no longer be
        // resized from the top.
        var dpi = GetDpiForWindow(_handle);
        var border = GetSystemMetricsForDpi(SmCxSizeFrame, dpi)
                   + GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);

        var width = (int)Math.Round(DragRegion.ActualWidth * scale);
        var height = (int)Math.Round(DragRegion.ActualHeight * scale);

        source.SetRegionRects(NonClientRegionKind.Caption, new[]
        {
            new RectInt32(border, border, Math.Max(0, width - (border * 2)),
                          Math.Max(0, height - border)),
        });

        Rect bounds = CloseButton
            .TransformToVisual(null)
            .TransformBounds(new Rect(0, 0, CloseButton.ActualWidth, CloseButton.ActualHeight));

        source.SetRegionRects(NonClientRegionKind.Passthrough, new[] { Scale(bounds, scale) });
    }

    private static RectInt32 Scale(Rect r, double scale) => new(
        (int)Math.Round(r.X * scale),
        (int)Math.Round(r.Y * scale),
        (int)Math.Round(r.Width * scale),
        (int)Math.Round(r.Height * scale));

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

    /// <summary>
    /// Opens at the design size, centred on the display the window landed on.
    /// </summary>
    /// <remarks>
    /// AppWindow works in physical pixels while XAML lays out in DIPs, so the
    /// requested size is scaled by the monitor's DPI - otherwise the window
    /// comes up two thirds of its intended size on a 150% display.
    /// </remarks>
    private void SizeAndCentre(int logicalWidth, int logicalHeight)
    {
        var dpi = GetDpiForWindow(_handle);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var width = (int)Math.Round(logicalWidth * scale);
        var height = (int)Math.Round(logicalHeight * scale);

        DisplayArea display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        RectInt32 work = display.WorkArea;

        // Shrink to fit smaller displays, keeping a margin so the window still
        // reads as a window. The minimum is applied last: a display too small
        // for even that is better served by a clipped window than by one
        // placed off-screen.
        width = Math.Min(width, (int)(work.Width * MaxWorkAreaFraction));
        height = Math.Min(height, (int)(work.Height * MaxWorkAreaFraction));

        width = Math.Max(width, Math.Min((int)Math.Round(MinimumWidth * scale), work.Width));
        height = Math.Max(height, Math.Min((int)Math.Round(MinimumHeight * scale), work.Height));

        var x = work.X + ((work.Width - width) / 2);
        var y = work.Y + ((work.Height - height) / 2);

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
