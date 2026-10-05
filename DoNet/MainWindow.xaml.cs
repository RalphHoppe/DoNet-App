using System;
using System.Runtime.InteropServices;
using DoNet.Contracts;
using DoNet.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

// Microsoft.UI and Windows.UI both expose a "Colors" type, so neither is imported;
// the few colour literals below are written out in full instead.

namespace DoNet;

/// <summary>
/// The shell window: a chrome-free white frame that hosts every page.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DesignWidth = 1280;
    private const int DesignHeight = 810;

    public MainWindow()
    {
        InitializeComponent();

        ConfigureTitleBar();
        SizeAndCentre(DesignWidth, DesignHeight);

        var navigation = App.Current.Services.GetRequiredService<INavigationService>();
        navigation.Frame = RootFrame;
        navigation.NavigateTo(typeof(SplashPage));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>
    /// Hides the system title bar and recolours the caption buttons so they disappear
    /// into the white background instead of sitting in a grey strip.
    /// </summary>
    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        var titleBar = AppWindow.TitleBar;

        titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 0x5C, 0x60, 0x66);
        titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 0xA0, 0xA5, 0xAB);
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(255, 0xF0, 0xF1, 0xF2);
        titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0x2B, 0x2F, 0x33);
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(255, 0xE4, 0xE6, 0xE9);
        titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 0x2B, 0x2F, 0x33);
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
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(handle);
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
