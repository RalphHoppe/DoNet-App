using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DoNet.ViewModels;

/// <summary>
/// Backs the lock screen, which stands in front of the app on every launch once a
/// password has been set.
/// </summary>
public partial class LockViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Asked to verify a password; returns true if it was correct.
    /// </summary>
    /// <remarks>
    /// Nothing subscribes yet. This is the seam the vault work plugs into - deriving the
    /// key from the password and letting the AES-GCM tag decide whether it was right -
    /// so no crypto has to leak into the view.
    /// </remarks>
    public event Func<string, Task<bool>>? Unlocking;

    /// <summary>
    /// Raised when "Forgot password?" is clicked. Also unsubscribed: what this should do
    /// is a product decision, not a technical one.
    /// </summary>
    public event Action? ForgotPasswordRequested;

    /// <summary>True once a wrong password has been reported.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            // With no verifier wired up there is nothing to check against, so the field
            // is left untouched rather than being waved through.
            if (Unlocking is not { } verify)
            {
                return;
            }

            if (await verify(Password))
            {
                Password = string.Empty;
                return;
            }

            // Only the password is cleared, not the error: the user needs to see why
            // nothing happened, and retyping is faster than correcting a hidden string.
            Password = string.Empty;
            ErrorMessage = "Incorrect password.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not unlock. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ForgotPassword() => ForgotPasswordRequested?.Invoke();

    private bool CanUnlock() => !IsBusy && Password.Length > 0;

    /// <summary>
    /// Clears the error as soon as the user starts over, so the red border does not
    /// follow them into their next attempt.
    /// </summary>
    partial void OnPasswordChanged(string value)
    {
        if (value.Length > 0)
        {
            ErrorMessage = null;
        }
    }
}
