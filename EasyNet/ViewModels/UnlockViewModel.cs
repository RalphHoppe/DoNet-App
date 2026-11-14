using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyNet.Services;
using EasyNet.Views;

namespace EasyNet.ViewModels;

/// <summary>
/// The unlock screen's mind: take the password, ask the vault, and either
/// open or say why not - once, quietly, without wiping what was typed until
/// the answer is in.
/// </summary>
public sealed partial class UnlockViewModel : ObservableObject
{
    private readonly IVaultService _vault;
    private readonly INavigationService _navigation;

    private string _password = string.Empty;

    /// <summary>True while the password is being tried, so nothing is asked twice.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    /// <summary>True while nothing is being tried - the action's own enabled word.</summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>True after a wrong password, until the next word is typed.</summary>
    [ObservableProperty]
    private bool _hasError;

    public UnlockViewModel(IVaultService vault, INavigationService navigation)
    {
        _vault = vault;
        _navigation = navigation;
    }

    /// <summary>The field's word. Typing clears the verdict of the last try.</summary>
    public void SetPassword(string value)
    {
        _password = value;
        HasError = false;
    }

    /// <summary>Tries the password against the vault.</summary>
    /// <remarks>
    /// The vault's own verdicts come back as booleans; anything unexpected is
    /// folded into the same honest answer - a vault that cannot be asked is a
    /// vault that does not open - and the busy flag is cleared no matter
    /// what, because the one thing worse than a refused password is an
    /// action that quietly stops answering.
    ///</remarks>
    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        bool opened;
        try
        {
            opened = await _vault.TryUnlockAsync(_password);
        }
        catch
        {
            opened = false;
        }
        finally
        {
            IsBusy = false;
        }

        if (opened)
        {
            _password = string.Empty;
            _navigation.NavigateTo(typeof(VaultPage));
        }
        else
        {
            HasError = true;
        }
    }
}
