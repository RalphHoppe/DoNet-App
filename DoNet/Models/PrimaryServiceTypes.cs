using System;
using DoNet.Controls;
using Windows.UI;

namespace DoNet.Models;

/// <summary>The four primary service types, the ones with hand-built screens.</summary>
public enum PrimaryServiceType
{
    Vps,
    Proxy,
    Domain,
    Wallets,
}

/// <summary>
/// Everything the v2 services screens know about the four primary types.
/// </summary>
/// <remarks>
/// The types are code, not rows: their screens are hand-built, so their names
/// and colours are as fixed as the screens themselves. Service types the user
/// adds later are rows and flow through the dynamic designer - this catalog is
/// only for the primaries.
///</remarks>
public static class PrimaryServiceTypes
{
    /// <summary>The type's name as the UI shows it - labels uppercase it.</summary>
    public static string NameOf(PrimaryServiceType kind) => kind switch
    {
        PrimaryServiceType.Vps => "VPS",
        PrimaryServiceType.Proxy => "Proxy",
        PrimaryServiceType.Domain => "Domain",
        PrimaryServiceType.Wallets => "Wallets",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The gradient's light stop, at the top-left corner.</summary>
    public static string LightOf(PrimaryServiceType kind) => kind switch
    {
        PrimaryServiceType.Vps => "#FF5F57",
        PrimaryServiceType.Proxy => "#28C840",
        PrimaryServiceType.Domain => "#3B82F7",
        PrimaryServiceType.Wallets => "#FEBC2E",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The gradient's dark stop, at the bottom-right corner.</summary>
    public static string DarkOf(PrimaryServiceType kind) => kind switch
    {
        PrimaryServiceType.Vps => "#FF5F57",
        PrimaryServiceType.Proxy => "#14621F",
        PrimaryServiceType.Domain => "#234C91",
        PrimaryServiceType.Wallets => "#C37D2C",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The type's gradient as a brush - top-left to bottom-right, as the design
    /// specifies. A fresh instance per call: a brush is a dependency object with
    /// one place in the tree, and the rail, the header and the button all want
    /// their own.
    /// </summary>
    public static Microsoft.UI.Xaml.Media.LinearGradientBrush GradientOf(PrimaryServiceType kind)
    {
        return new Microsoft.UI.Xaml.Media.LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new Microsoft.UI.Xaml.Media.GradientStop
                {
                    Color = ColorFromHex(LightOf(kind)),
                    Offset = 0,
                },
                new Microsoft.UI.Xaml.Media.GradientStop
                {
                    Color = ColorFromHex(DarkOf(kind)),
                    Offset = 1,
                },
            },
        };
    }

    /// <summary>
    /// The white every type icon is drawn in on its gradient tile. Built on the
    /// struct's channels because the WinRT colour helpers are not projected for
    /// C# consumers - <c>ColorHelper</c> and <c>Colors</c> do not exist for
    /// this app, and each has cost a build.
    /// </summary>
    public static Color IconWhite => new Color
    {
        A = 255,
        R = 255,
        G = 255,
        B = 255,
    };

    /// <summary>
    /// <c>#RRGGBB</c> as a colour. WinUI has no parser for the string form and
    /// <c>ColorHelper</c> is not projected for C# consumers, so the struct's
    /// channels are set by hand - the only format this catalog ever holds.
    /// </summary>
    private static Color ColorFromHex(string hex)
    {
        return new Color
        {
            A = 255,
            R = Convert.ToByte(hex.Substring(1, 2), 16),
            G = Convert.ToByte(hex.Substring(3, 2), 16),
            B = Convert.ToByte(hex.Substring(5, 2), 16),
        };
    }

    /// <summary>The icon paths for the type's mark; see <see cref="TypeMarkData"/>.</summary>
    public static string[] MarkOf(PrimaryServiceType kind) => kind switch
    {
        PrimaryServiceType.Vps => TypeMarkData.Vps,
        PrimaryServiceType.Proxy => TypeMarkData.Proxy,
        PrimaryServiceType.Domain => TypeMarkData.Domain,
        PrimaryServiceType.Wallets => TypeMarkData.Wallets,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>The mark's native box, which <see cref="Controls.TypeMark"/> scales from.</summary>
    public static (double Width, double Height) MarkBoxOf(PrimaryServiceType kind) => kind switch
    {
        PrimaryServiceType.Vps => (30, 28),
        PrimaryServiceType.Proxy => (32, 28),
        PrimaryServiceType.Domain => (32, 32),
        PrimaryServiceType.Wallets => (32, 32),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
