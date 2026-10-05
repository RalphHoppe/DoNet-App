using System;
using Microsoft.UI.Xaml.Controls;

namespace DoNet.Contracts;

/// <summary>
/// A thin wrapper over the shell <see cref="Frame"/> so pages and view models can move
/// around without reaching into the window.
/// </summary>
public interface INavigationService
{
    /// <summary>The frame being driven. Assigned once by the shell when it is created.</summary>
    Frame? Frame { get; set; }

    bool CanGoBack { get; }

    /// <summary>Raised after a successful navigation, with the page type navigated to.</summary>
    event EventHandler<Type>? Navigated;

    /// <summary>
    /// Navigates to <paramref name="pageType"/>.
    /// </summary>
    /// <param name="clearBackStack">
    /// For one-way transitions such as splash to onboarding, so Back cannot return to a
    /// page that no longer makes sense.
    /// </param>
    bool NavigateTo(Type pageType, object? parameter = null, bool clearBackStack = false);

    bool GoBack();
}
