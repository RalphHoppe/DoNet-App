using System;
using DoNet.Contracts;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Services;

/// <inheritdoc cref="INavigationService" />
public sealed class NavigationService : INavigationService
{
    public Frame? Frame { get; set; }

    public bool CanGoBack => Frame?.CanGoBack ?? false;

    public event EventHandler<Type>? Navigated;

    public bool NavigateTo(Type pageType, object? parameter = null, bool clearBackStack = false)
    {
        ArgumentNullException.ThrowIfNull(pageType);

        if (Frame is null || (Frame.Content?.GetType() == pageType && parameter is null))
        {
            return false;
        }

        // Pages own their own entrance animation, so the frame's built-in slide is
        // suppressed to stop the two fighting each other.
        if (!Frame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo()))
        {
            return false;
        }

        if (clearBackStack)
        {
            Frame.BackStack.Clear();
        }

        Navigated?.Invoke(this, pageType);
        return true;
    }

    public bool GoBack()
    {
        if (!CanGoBack)
        {
            return false;
        }

        Frame!.GoBack();
        Navigated?.Invoke(this, Frame.Content?.GetType() ?? typeof(object));
        return true;
    }
}
