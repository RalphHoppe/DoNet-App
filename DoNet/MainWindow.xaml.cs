using System;
using System.Runtime.InteropServices;
using DoNet.Contracts;
using DoNet.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Foundation;
using Windows.Graphics;

namespace DoNet;

/// <summary>
/// The shell window: a chrome-free white frame that hosts every page.
/// </summary>
public sealed partial class MainWindow : Window
{
    // The design's own artboard. The window opens at this size where the display has
    // room, so the app matches the design 1:1 without anyone having to scale anything.
    private const int DesignWidth = 1440;
    private const int DesignHeight = 900;

    // ...but never edge to edge. On a 1366x768 laptop the full design height does not
    // fit at all, and a window exactly the size of the work area reads as a botched
    // maximise rather than a deliberate default.
    private const double MaxWorkAreaFraction = 0.92;

    // The smallest window the layout still works in, derived rather than picked:
    //
    //   width  - the 352px content column plus a 64px margin either side.
    //   height - the tallest screen's content block is 351px and sits 24px above
    //            centre, so clearing the 48px caption strip by a comfortable margin
    //            needs (H - 351) / 2 - 24 >= 64, i.e. 527. Rounded up.
    //
    // Below this the content would start colliding with the caption buttons.
    private const int MinimumWidth = 480;
    private const int MinimumHeight = 540;

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

        // The XamlRoot does not exist until the tree goes live, so the scale watch
        // cannot be attached from here.
        RootFrame.Loaded += OnRootFrameLoaded;

        // The caption region has to be re-declared whenever its pixel geometry moves:
        // on first layout, on every resize, and on a DPI change.
        DragRegion.Loaded += (_, _) => UpdateCaptionRegions();
        DragRegion.SizeChanged += (_, _) => UpdateCaptionRegions();

        var navigation = App.Current.Services.GetRequiredService<INavigationService>();
        navigation.Frame = RootFrame;
        navigation.Navigated += OnNavigated;
        navigation.NavigateTo(typeof(SplashPage));
    }

    /// <summary>
    /// Shows the title bar's logo on the home screen and nowhere else. The other pages
    /// each draw their own large lockup, and the two together look like a mistake.
    /// </summary>
    private void OnNavigated(object? sender, Type pageType) =>
        TitleLogo.Visibility = pageType == typeof(HomePage)
            ? Visibility.Visible
            : Visibility.Collapsed;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    /// <summary>
    /// Removes the system title bar outright, leaving the caption buttons in
    /// MainWindow.xaml as the only window chrome.
    /// </summary>
    /// <remarks>
    /// Extending content into the title bar is not enough here: that keeps the system
    /// caption buttons, which cannot be hidden and would sit exactly where the coloured
    /// dots go. Dropping the title bar is the only way to be rid of them. The border is
    /// kept, so the window still has its resize edges and drop shadow.
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
    /// The limits are given in physical pixels and the presenter does not scale them,
    /// so they are multiplied by the monitor's DPI here - on a 150% display an
    /// unscaled 480 would really mean 320 and let the window shrink past the point the
    /// content fits. Maximising is deliberately left alone; only the floor is fixed.
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
    /// The presenter keeps the pixel values it was given across a DPI change, which
    /// silently loosens or tightens the limit by the ratio between the two monitors.
    /// This fires for ordinary size changes too, hence the scale comparison.
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
    /// Tells the window manager that the top strip is the caption, and that the three
    /// coloured dots inside it are not.
    /// </summary>
    /// <remarks>
    /// This replaces an earlier attempt that emulated dragging by calling
    /// ReleaseCapture and posting WM_NCLBUTTONDOWN/HTCAPTION from PointerPressed. That
    /// approach cannot work properly: WM_NCLBUTTONDOWN runs a *modal* move loop inside
    /// the message, so the handler does not return until the drag ends, XAML's input
    /// state is left stale afterwards, and the second click of a double-click is eaten
    /// by the move loop instead of arriving as a second press - which is why maximise
    /// worked only intermittently.
    /// <para>
    /// Declaring the region instead hands drag, double-click maximise, right-click
    /// system menu, edge snapping and multi-monitor handoff back to the window
    /// manager, which is the only thing that implements them correctly. This is the
    /// documented companion to SetBorderAndTitleBar(true, false): the presenter hides
    /// the system caption, and this says where the caption actually is.
    /// </para>
    /// <para>
    /// Rects are in physical pixels relative to the client area, hence the
    /// RasterizationScale. Passthrough wins over Caption where the two overlap, which
    /// is what keeps the dots clickable and their hover states alive.
    /// </para>
    /// </remarks>
    private void UpdateCaptionRegions()
    {
        if (DragRegion.XamlRoot is not { } root || DragRegion.ActualWidth <= 0)
        {
            return;
        }

        var scale = root.RasterizationScale;
        var source = InputNonClientPointerSource.GetForWindowId(AppWindow.Id);

        // Hold the caption off the window's own resize edges. A caption region claims
        // every pixel it covers, so running it to x=0 and y=0 would take the top-left
        // grab handles with it and the window could no longer be resized from the top.
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

        var bounds = CaptionButtons
            .TransformToVisual(null)
            .TransformBounds(new Rect(0, 0, CaptionButtons.ActualWidth, CaptionButtons.ActualHeight));

        source.SetRegionRects(NonClientRegionKind.Passthrough, new[] { Scale(bounds, scale) });
    }

    private static RectInt32 Scale(Rect r, double scale) => new(
        (int)Math.Round(r.X * scale),
        (int)Math.Round(r.Y * scale),
        (int)Math.Round(r.Width * scale),
        (int)Math.Round(r.Height * scale));

    private void OnMinimizeClick(object sender, RoutedEventArgs args)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs args) => ToggleMaximised();

    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();

    private void ToggleMaximised()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        if (presenter.State == OverlappedPresenterState.Maximized)
        {
            presenter.Restore();
        }
        else
        {
            presenter.Maximize();
        }
    }

    /// <summary>
    /// Opens at the design size, centred on the display the window landed on.
    /// </summary>
    /// <remarks>
    /// AppWindow works in physical pixels while XAML lays out in DIPs, so the requested
    /// size is scaled by the monitor's DPI - otherwise the window comes up two thirds
    /// of its intended size on a 150% display.
    /// </remarks>
    private void SizeAndCentre(int logicalWidth, int logicalHeight)
    {
        var dpi = GetDpiForWindow(_handle);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var width = (int)Math.Round(logicalWidth * scale);
        var height = (int)Math.Round(logicalHeight * scale);

        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var work = display.WorkArea;

        // Shrink to fit smaller displays, keeping a margin so the window still reads as
        // a window. The minimum is applied last: a display too small for even that is
        // better served by a clipped window than by one placed off-screen.
        width = Math.Min(width, (int)(work.Width * MaxWorkAreaFraction));
        height = Math.Min(height, (int)(work.Height * MaxWorkAreaFraction));

        width = Math.Max(width, Math.Min((int)Math.Round(MinimumWidth * scale), work.Width));
        height = Math.Max(height, Math.Min((int)Math.Round(MinimumHeight * scale), work.Height));

        var x = work.X + ((work.Width - width) / 2);
        var y = work.Y + ((work.Height - height) / 2);

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
