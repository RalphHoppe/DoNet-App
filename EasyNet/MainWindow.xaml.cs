using System;
using System.Runtime.InteropServices;
using EasyNet.Services;
using EasyNet.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Graphics;

namespace EasyNet;

/// <summary>
/// The shell window: a chrome-free white card that hosts every page.
/// </summary>
/// <remarks>
/// The window is fixed: no title bar, no resize border, no thin grey frame
/// and no system shadow - the pages own every pixel - and it opens at the
/// design's 800 x 450, centred, never running edge to edge on a display
/// that cannot hold it.
///</remarks>
public sealed partial class MainWindow : Window
{
    // The design's window: the handoff's reference width, 450 tall.
    private const int DesignWidth = 800;
    private const int DesignHeight = 450;

    // ...but never edge to edge.
    private const double MaxWorkAreaFraction = 0.92;

    // DWM's window corner preference: attribute 33, value 2 = round.
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;

    private readonly IntPtr _handle;

    /// <summary>Last known scale factor, to spot the window moving to another monitor.</summary>
    private double _lastScale;

    public MainWindow()
    {
        InitializeComponent();

        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ConfigureWindowChrome();
        SizeAndCentre(DesignWidth, DesignHeight);

        // The X belongs to the app's screens, not to the brand's moment: it
        // appears the first time a real page takes the frame over.
        RootFrame.Navigated += OnRootFrameNavigated;

        // The caption region has to be re-declared whenever its pixel geometry
        // moves: on first layout, on size changes, and on a DPI change.
        DragRegion.Loaded += (_, _) => UpdateCaptionRegions();
        DragRegion.SizeChanged += (_, _) => UpdateCaptionRegions();

        // The XamlRoot does not exist until the tree goes live, so the scale
        // watch cannot be attached from here.
        RootFrame.Loaded += OnRootFrameLoaded;

        INavigationService navigation = App.Current.Services.GetRequiredService<INavigationService>();
        navigation.Frame = RootFrame;
        navigation.NavigateTo(typeof(SplashPage));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    /// <summary>
    /// Removes the system chrome outright and fixes the window's size: no
    /// title bar, no border, no resize edges, no shadow.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The border and the shadow come as a pair - the DWM shadow is drawn
    /// around the frame - so taking the frame takes both, which is exactly
    /// what was asked. Losing the resize edges costs nothing to a window
    /// that no longer resizes.
    /// </para>
    /// <para>
    /// Rounded corners are DWM's to give: Windows 11 honours the request
    /// with its own radius, and older systems simply keep the window square.
    /// The platform offers no custom radius, and the tricks that fake one
    /// break windows drawn the way this one is - so the request is made and
    /// nothing more is forced.
    /// </para>
    /// </remarks>
    private void ConfigureWindowChrome()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
            presenter.IsResizable = false;
        }

        int round = DwmwcpRound;
        _ = DwmSetWindowAttribute(_handle, DwmwaWindowCornerPreference, ref round, sizeof(int));
    }

    /// <summary>
    /// Shows the X once the splash has handed the window to a real screen.
    /// </summary>
    private void OnRootFrameNavigated(object sender, NavigationEventArgs args) =>
        CloseButton.Visibility = args.SourcePageType == typeof(SplashPage)
            ? Visibility.Collapsed
            : Visibility.Visible;

    private void OnRootFrameLoaded(object sender, RoutedEventArgs args)
    {
        RootFrame.Loaded -= OnRootFrameLoaded;

        if (RootFrame.XamlRoot is { } xamlRoot)
        {
            xamlRoot.Changed += OnXamlRootChanged;
        }
    }

    /// <summary>
    /// Re-declares the caption when the window moves to a monitor with a
    /// different scale factor, so its rects stay where the strip is.
    /// </summary>
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (Math.Abs(sender.RasterizationScale - _lastScale) > 0.001)
        {
            _lastScale = sender.RasterizationScale;
            UpdateCaptionRegions();
        }
    }

    /// <summary>
    /// Tells the window manager that the top strip is the caption, and that
    /// the close button inside it is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declaring the region hands dragging and the right-click system menu
    /// back to the window manager, which is the only thing that implements
    /// them correctly.
    /// </para>
    /// <para>
    /// Rects are in physical pixels relative to the client area, hence the
    /// RasterizationScale. With no resize frame there is nothing along the
    /// edges to protect, so the caption runs the full width of the strip.
    /// Passthrough wins over Caption where the two overlap, which is what
    /// keeps the close button clickable and its hover state alive.
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

        var width = (int)Math.Round(DragRegion.ActualWidth * scale);
        var height = (int)Math.Round(DragRegion.ActualHeight * scale);

        source.SetRegionRects(NonClientRegionKind.Caption, new[]
        {
            new RectInt32(0, 0, Math.Max(0, width), Math.Max(0, height)),
        });

        if (CloseButton.Visibility != Visibility.Visible)
        {
            // Nothing to carve out yet - and a previously carved region
            // should not outlive the button it was for.
            source.SetRegionRects(NonClientRegionKind.Passthrough, new[] { new RectInt32(0, 0, 0, 0) });
            return;
        }

        Rect bounds = CloseButton
            .TransformToVisual(null)
            .TransformBounds(new Rect(0, 0, CloseButton.ActualWidth, CloseButton.ActualHeight));

        source.SetRegionRects(NonClientRegionKind.Passthrough, new[] { Scale(bounds, scale) });
    }

    private static RectInt32 Scale(Rect r, double scale) => new(
        (int)Math.Round(r.X * scale), (int)Math.Round(r.Y * scale),
        (int)Math.Round(r.Width * scale), (int)Math.Round(r.Height * scale));

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
        // reads as a window.
        width = Math.Min(width, (int)(work.Width * MaxWorkAreaFraction));
        height = Math.Min(height, (int)(work.Height * MaxWorkAreaFraction));

        var x = work.X + ((work.Width - width) / 2);
        var y = work.Y + ((work.Height - height) / 2);

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
