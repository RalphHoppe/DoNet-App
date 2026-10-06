using System;
using System.Runtime.InteropServices;
using DoNet.Contracts;
using DoNet.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;

namespace DoNet;

/// <summary>
/// The shell window: a chrome-free white frame that hosts every page.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DesignWidth = 1280;
    private const int DesignHeight = 810;

    private const uint WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    private readonly IntPtr _handle;

    /// <summary>When the title bar was last pressed, for detecting a double-click.</summary>
    private DateTime _lastTitleBarPress = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ConfigureTitleBar();
        SizeAndCentre(DesignWidth, DesignHeight);

        var navigation = App.Current.Services.GetRequiredService<INavigationService>();
        navigation.Frame = RootFrame;
        navigation.NavigateTo(typeof(SplashPage));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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
    /// Starts a window drag, or maximises on a double-click.
    /// </summary>
    /// <remarks>
    /// The drag is handed straight to the window manager rather than being emulated by
    /// moving the window on pointer events. That is what keeps snapping, multi-monitor
    /// handoff and restore-on-drag behaving exactly as they do for a real title bar.
    /// <para>
    /// The double-click has to be detected here as well. The window manager only
    /// maximises on a genuine WM_NCLBUTTONDBLCLK, and synthesising a button-down per
    /// press means it never sees one - it sees two unrelated clicks.
    /// </para>
    /// </remarks>
    private void OnDragRegionPointerPressed(object sender, PointerRoutedEventArgs args)
    {
        // Buttons mark their own pointer events handled, so a click on a caption dot
        // never reaches this handler.
        if (!args.GetCurrentPoint(DragRegion).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if ((now - _lastTitleBarPress).TotalMilliseconds <= GetDoubleClickTime())
        {
            _lastTitleBarPress = DateTime.MinValue;
            ToggleMaximised();
            return;
        }

        _lastTitleBarPress = now;

        ReleaseCapture();
        SendMessage(_handle, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
    }

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

        // clamp so the window still fits on smaller screens
        width = Math.Min(width, work.Width);
        height = Math.Min(height, work.Height);

        var x = work.X + ((work.Width - width) / 2);
        var y = work.Y + ((work.Height - height) / 2);

        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
