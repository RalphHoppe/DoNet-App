using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DoNet.ViewModels;

/// <summary>
/// Backs the "Create a password for your DoNet account" screen.
/// </summary>
public partial class CreatePasswordViewModel : ObservableObject
{
    /// <summary>
    /// Shortest master password we will accept.
    /// </summary>
    /// <remarks>
    /// This is the only thing protecting the vault, and it is never transmitted, so the
    /// usual "complexity rules" do more harm than good - length is what matters. NIST
    /// SP 800-63B recommends a length floor and no composition rules.
    /// </remarks>
    public const int MinimumPasswordLength = 8;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasPasswordError;

    [ObservableProperty]
    private bool _hasConfirmError;

    /// <summary>
    /// Raised once both fields pass validation, with the accepted password.
    /// </summary>
    /// <remarks>
    /// Nothing subscribes yet. This is the seam the vault work plugs into: deriving the
    /// key, creating the encrypted database and moving on to the next screen all hang
    /// off this callback, so none of that has to leak into the view.
    /// </remarks>
    public event Func<string, Task>? Submitted;

    /// <summary>True while the red notice should replace the amber one.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Validates the pair and hands the password to <see cref="Submitted"/>.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync()
    {
        ClearErrors();

        if (Password.Length < MinimumPasswordLength)
        {
            HasPasswordError = true;
            ErrorMessage = $"Use at least {MinimumPasswordLength} characters for your password.";
            return;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            HasConfirmError = true;
            ErrorMessage = "Those passwords do not match.";
            return;
        }

        IsBusy = true;

        try
        {
            if (Submitted is { } handler)
            {
                await handler(Password);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not create your account. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// The button stays live as soon as both fields have content; the actual rules are
    /// reported on submit so the user is not scolded mid-keystroke.
    /// </summary>
    private bool CanSubmit() => !IsBusy && Password.Length > 0 && ConfirmPassword.Length > 0;

    partial void OnPasswordChanged(string value) => ClearErrors();

    partial void OnConfirmPasswordChanged(string value) => ClearErrors();

    private void ClearErrors()
    {
        ErrorMessage = null;
        HasPasswordError = false;
        HasConfirmError = false;
    }
}
