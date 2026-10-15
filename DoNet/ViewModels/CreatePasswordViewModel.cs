using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;
using DoNet.Views;

namespace DoNet.ViewModels;

/// <summary>
/// Backs the "Create a password for your DoNet account" screen, shown once per install.
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

    /// <summary>How long the success notice stays up before the lock screen takes over.</summary>
    private const int SuccessPauseMs = 1400;

    private readonly IVaultService _vault;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyPropertyChangedFor(nameof(SubmitLabel))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError), nameof(ShowError), nameof(ShowGuidance))]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyPropertyChangedFor(nameof(ShowSuccess), nameof(ShowError), nameof(ShowGuidance))]
    private bool _isSuccess;

    [ObservableProperty]
    private bool _hasPasswordError;

    [ObservableProperty]
    private bool _hasConfirmError;

    public CreatePasswordViewModel(IVaultService vault, INavigationService navigation)
    {
        _vault = vault;
        _navigation = navigation;
    }

    /// <summary>True while the red notice should replace the amber one.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // The notice slot holds one of three things and never changes height, so the button
    // below it cannot move. Exactly one of these is true at any moment.
    public bool ShowSuccess => IsSuccess;

    public bool ShowError => !IsSuccess && HasError;

    public bool ShowGuidance => !IsSuccess && !HasError;

    /// <summary>
    /// Key derivation is deliberately slow, so the button says what it is doing rather
    /// than just going flat for a second.
    /// </summary>
    public string SubmitLabel => IsBusy ? "CREATING..." : "CREATE AND CONTINUE";

    /// <summary>
    /// Validates the pair, creates the encrypted vault, and hands over to the lock screen.
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
            await _vault.CreateAsync(Password);

            IsSuccess = true;
            await Task.Delay(SuccessPauseMs);

            // Cleared before leaving rather than left sitting in a bound property.
            Password = string.Empty;
            ConfirmPassword = string.Empty;

            // No way back: the create screen is finished with for the life of the install.
            _navigation.NavigateTo(typeof(WelcomePage), clearBackStack: true);
        }
        catch (Exception ex)
        {
            IsSuccess = false;
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
    private bool CanSubmit() => !IsBusy && !IsSuccess && Password.Length > 0 && ConfirmPassword.Length > 0;

    partial void OnPasswordChanged(string value) => ClearErrors();

    partial void OnConfirmPasswordChanged(string value) => ClearErrors();

    private void ClearErrors()
    {
        ErrorMessage = null;
        HasPasswordError = false;
        HasConfirmError = false;
    }
}
