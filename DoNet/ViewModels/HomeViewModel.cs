using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;
using DoNet.Views;

namespace DoNet.ViewModels;

/// <summary>The four destinations in the home screen's navigation rail.</summary>
public enum HomeSection
{
    Services,
    Persons,
    Sites,
    Accounts,
}

/// <summary>
/// Backs the home screen: which rail item is selected, and locking the app.
/// </summary>
/// <remarks>
/// There is one command per section rather than a single command taking the section as
/// a parameter. XAML types CommandParameter as object and never converts the literal to
/// the enum, so a shared command would have to accept a string and parse it - trading a
/// compile-time error for a silent no-op on a typo.
/// </remarks>
public partial class HomeViewModel : ObservableObject
{
    private readonly INavigationService _navigation;
    private readonly IVaultService _vault;
    private readonly PersonsViewModel _persons;
    private readonly WebsitesViewModel _websites;
    private readonly AccountsViewModel _accounts;
    private readonly ServicesViewModel _services;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServicesSelected))]
    [NotifyPropertyChangedFor(nameof(IsAccountsSelected))]
    [NotifyPropertyChangedFor(nameof(IsPersonsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSitesSelected))]
    // Persons, not Services: the supplied artwork shows Persons as the selected
    // destination, and the home screen is built to that artwork. Services has a
    // surface now, but it stays one rail stop away.
    private HomeSection _selectedSection = HomeSection.Persons;

    public HomeViewModel(
        INavigationService navigation,
        IVaultService vault,
        PersonsViewModel persons,
        WebsitesViewModel websites,
        AccountsViewModel accounts,
        ServicesViewModel services)
    {
        _navigation = navigation;
        _vault = vault;
        _persons = persons;
        _websites = websites;
        _accounts = accounts;
        _services = services;
    }

    public bool IsServicesSelected => SelectedSection == HomeSection.Services;

    public bool IsPersonsSelected => SelectedSection == HomeSection.Persons;

    public bool IsSitesSelected => SelectedSection == HomeSection.Sites;

    public bool IsAccountsSelected => SelectedSection == HomeSection.Accounts;

    [RelayCommand]
    private void SelectServices() => SelectedSection = HomeSection.Services;

    [RelayCommand]
    private void SelectPersons() => SelectedSection = HomeSection.Persons;

    [RelayCommand]
    private void SelectSites() => SelectedSection = HomeSection.Sites;

    [RelayCommand]
    private void SelectAccounts() => SelectedSection = HomeSection.Accounts;

    /// <summary>
    /// Returns to the lock screen. The back stack is cleared so Back cannot walk back
    /// into the unlocked app.
    /// </summary>
    [RelayCommand]
    private void Lock()
    {
        // Locking has to mean something now that the records are in an encrypted file.
        // Dropping the data key closes the database; clearing the directory removes the
        // decrypted copies that were sitting in the grid. Navigating alone would leave
        // every record on screen behind the lock screen, in memory and one Back away.
        _persons.Reset();
        _websites.Reset();
        _accounts.Reset();
        _services.Reset();
        _vault.Lock();

        _navigation.NavigateTo(typeof(LockPage), clearBackStack: true);
    }
}
