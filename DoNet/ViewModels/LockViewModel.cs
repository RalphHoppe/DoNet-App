using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;
using DoNet.Views;

namespace DoNet.ViewModels;

/// <summary>
/// Backs the lock screen, which stands in front of the app on every launch once a
/// password has been set.
/// </summary>
public partial class LockViewModel : ObservableObject
{
    private readonly IVaultService _vault;
    private readonly INavigationService _navigation;

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

    public LockViewModel(IVaultService vault, INavigationService navigation)
    {
        _vault = vault;
        _navigation = navigation;
    }

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

                // Straight to the welcome screen, which plays and then hands over to
                // the home screen. The back stack is cleared so Back cannot return to
                // a lock screen the user has already got past.
                _navigation.NavigateTo(typeof(WelcomePage), clearBackStack: true);
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
    private void ForgotPassword() => _navigation.NavigateTo(typeof(ForgotPasswordPage));

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
