using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;
using DoNet.Views;

namespace DoNet.ViewModels;

/// <summary>
/// Backs the forgot-password screen, which offers the only thing that can be offered:
/// starting over.
/// </summary>
/// <remarks>
/// There is deliberately no recovery path. The master password is the only way to
/// unwrap the data key, and it is never stored, so a forgotten password means the data
/// is already unreadable - by us as much as by anyone else. This screen does not pretend
/// otherwise; it says so plainly and then clears the way for a new password.
/// </remarks>
public partial class ForgotPasswordViewModel : ObservableObject
{
    private readonly IVaultService _vault;
    private readonly INavigationService _navigation;
    private readonly IPersonDirectory _directory;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetCommand))]
    [NotifyPropertyChangedFor(nameof(ResetLabel))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public ForgotPasswordViewModel(
        IVaultService vault, INavigationService navigation, IPersonDirectory directory)
    {
        _vault = vault;
        _navigation = navigation;
        _directory = directory;
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string ResetLabel => IsBusy ? "RESETTING..." : "RESET";

    /// <summary>
    /// Destroys the vault and sends the user back to onboarding as a first run.
    /// </summary>
    /// <remarks>
    /// No confirmation dialog: this screen is the confirmation. Reaching it takes a
    /// deliberate click from the lock screen, and it explains the consequence twice
    /// before offering the button.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanReset))]
    private async Task ResetAsync()
    {
        ErrorMessage = null;
        IsBusy = true;

        try
        {
            // Close first. Windows will not delete a file that something still has
            // open, and the reset has to remove the database - so a live connection
            // would turn this into a half-finished reset.
            await _directory.CloseAsync();

            await _vault.ResetAsync();

            _navigation.NavigateTo(typeof(CreatePasswordPage), clearBackStack: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not reset. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// A fresh lock screen rather than a back-navigation, so a half-typed password from
    /// the previous visit is not still sitting there.
    /// </summary>
    [RelayCommand]
    private void BackToLock() => _navigation.NavigateTo(typeof(LockPage), clearBackStack: true);

    private bool CanReset() => !IsBusy;
}
