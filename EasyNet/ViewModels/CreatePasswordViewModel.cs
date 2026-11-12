using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyNet.Services;
using EasyNet.Views;

namespace EasyNet.ViewModels;

/// <summary>
/// The create-password screen's mind: hold the typed pair, judge them gently,
/// and hand a good pair to the vault.
/// </summary>
/// <remarks>
/// The password itself is pushed in from the field rather than bound, because
/// the box that holds it keeps its value out of the binding system on purpose
/// - the same reason the vault stores a verifier and not the password. This
/// view model is the only thing that ever sees the clear text, and it forgets
/// it the moment the vault is made.
///</remarks>
public sealed partial class CreatePasswordViewModel : ObservableObject
{
    private const int MinimumLength = 8;

    private readonly IVaultService _vault;
    private readonly INavigationService _navigation;

    private string _password = string.Empty;
    private string _confirm = string.Empty;

    /// <summary>True when the pair can be made into a vault password.</summary>
    [ObservableProperty]
    private bool _canCreate;

    /// <summary>True while typing has started but the password is still too short.</summary>
    [ObservableProperty]
    private bool _showLengthHint;

    /// <summary>True once the second field disagrees with the first.</summary>
    [ObservableProperty]
    private bool _showMismatchHint;

    /// <summary>True while the vault is being created, so nothing is asked twice.</summary>
    [ObservableProperty]
    private bool _isBusy;

    public CreatePasswordViewModel(IVaultService vault, INavigationService navigation)
    {
        _vault = vault;
        _navigation = navigation;
    }

    /// <summary>The first field's word.</summary>
    public void SetPassword(string value)
    {
        _password = value;
        Refresh();
    }

    /// <summary>The second field's word.</summary>
    public void SetConfirm(string value)
    {
        _confirm = value;
        Refresh();
    }

    /// <summary>Makes the vault - and the screen after it is the vault itself, unlocked.</summary>
    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!CanCreate)
        {
            return;
        }

        IsBusy = true;
        Refresh();

        await _vault.CreateAsync(_password);

        _password = string.Empty;
        _confirm = string.Empty;

        _navigation.NavigateTo(typeof(VaultPage));
    }

    /// <summary>
    /// One rule, said plainly, only when it is being broken: the hints are
    /// grey while the pair is merely unfinished and never block the typing
    /// they describe.
    /// </summary>
    private void Refresh()
    {
        CanCreate = !IsBusy
            && _password.Length >= MinimumLength
            && _confirm == _password;

        ShowLengthHint = _password.Length > 0 && _password.Length < MinimumLength;
        ShowMismatchHint = _confirm.Length > 0 && _confirm != _password;
    }
}
