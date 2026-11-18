namespace EasyNet;

/// <summary>
/// A screen that needs the window at a particular size says so, and the
/// shell listens when it arrives.
/// </summary>
/// <remarks>
/// The window is fixed - not resizable by hand - so its size is part of each
/// screen's design rather than the user's choice. Screens that do not say
/// otherwise get the shell's default, 800 x 450.
///</remarks>
public interface IPreferredWindowSize
{
    /// <summary>The window width the screen is designed for, in DIPs.</summary>
    int PreferredWidth { get; }

    /// <summary>The window height the screen is designed for, in DIPs.</summary>
    int PreferredHeight { get; }
}
