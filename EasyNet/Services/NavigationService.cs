using System;
using Microsoft.UI.Xaml.Controls;

namespace EasyNet.Services;

/// <summary>Moves the shell's frame between screens.</summary>
public interface INavigationService
{
    /// <summary>The shell's frame, handed over by the window that owns it.</summary>
    Frame? Frame { get; set; }

    /// <summary>Shows the page of the given type. Nothing happens without a frame.</summary>
    void NavigateTo(Type pageType);
}

/// <summary>
/// The frame the shell owns, reached through an interface so view models can
/// move between screens without knowing what a window is.
/// </summary>
public sealed class NavigationService : INavigationService
{
    /// <inheritdoc/>
    public Frame? Frame { get; set; }

    /// <inheritdoc/>
    public void NavigateTo(Type pageType)
    {
        if (Frame is not { } frame)
        {
            return;
        }

        // Navigating to the screen that is already up is a no-op, not a
        // reload: nothing about arriving where you already are should reset
        // what you were doing there.
        if (frame.Content?.GetType() == pageType)
        {
            return;
        }

        _ = frame.Navigate(pageType);
    }
}
