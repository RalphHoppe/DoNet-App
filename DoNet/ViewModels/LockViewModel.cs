using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;

namespace DoNet.ViewModels;

/// <summary>
/// Backs the lock screen, which stands in front of the app on every launch once a
/// password has been set.
/// </summary>
public partial class LockViewModel : ObservableObject
{
    private readonly IVaultService _vault;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    [NotifyPropertyChangedFor(nameof(SubmitLabel))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    /// <summary>
    /// Confirms the password was accepted.
    /// </summary>
    /// <remarks>
    /// A placeholder for a destination. Until there is a screen behind the lock screen,
    /// a correct password would otherwise clear the field and do nothing visible, which
    /// is indistinguishable from a bug. Delete this once <see cref="Unlocked"/> leads
    /// somewhere.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockCommand))]
    private bool _isUnlocked;

    public LockViewModel(IVaultService vault) => _vault = vault;

    /// <summary>
    /// Raised once the password has been accepted.
    /// </summary>
    /// <remarks>
    /// Nothing subscribes yet, because there is no screen behind the lock screen to
    /// navigate to. This is where that goes.
    /// </remarks>
    public event Action? Unlocked;

    /// <summary>
    /// Raised when "Forgot password?" is clicked. Also unsubscribed: what this should do
    /// is a product decision, not a technical one.
    /// </summary>
    public event Action? ForgotPasswordRequested;

    /// <summary>True once a wrong password has been reported.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string SubmitLabel => IsBusy ? "UNLOCKING..." : "CONTINUE";

    [RelayCommand(CanExecute = nameof(CanUnlock))]
    private async Task UnlockAsync()
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            if (await _vault.TryUnlockAsync(Password))
            {
                Password = string.Empty;
                IsUnlocked = true;
                Unlocked?.Invoke();
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

    private bool CanUnlock() => !IsBusy && !IsUnlocked && Password.Length > 0;

    /// <summary>
    /// Clears the error as soon as the user starts over, so the red border does not
    /// follow them into their next attempt.
    /// </summary>
    partial void OnPasswordChanged(string value)
    {
        if (value.Length > 0)
        {
            ErrorMessage = null;
            IsUnlocked = false;
        }
    }
}
