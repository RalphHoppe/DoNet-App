using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DoNet.Contracts;
using DoNet.Views;

namespace DoNet.ViewModels;

/// <summary>The three destinations in the home screen's navigation rail.</summary>
public enum HomeSection
{
    Services,
    Persons,
    Sites,
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsServicesSelected))]
    [NotifyPropertyChangedFor(nameof(IsPersonsSelected))]
    [NotifyPropertyChangedFor(nameof(IsSitesSelected))]
    private HomeSection _selectedSection = HomeSection.Services;

    public HomeViewModel(INavigationService navigation) => _navigation = navigation;

    public bool IsServicesSelected => SelectedSection == HomeSection.Services;

    public bool IsPersonsSelected => SelectedSection == HomeSection.Persons;

    public bool IsSitesSelected => SelectedSection == HomeSection.Sites;

    [RelayCommand]
    private void SelectServices() => SelectedSection = HomeSection.Services;

    [RelayCommand]
    private void SelectPersons() => SelectedSection = HomeSection.Persons;

    [RelayCommand]
    private void SelectSites() => SelectedSection = HomeSection.Sites;

    /// <summary>
    /// Returns to the lock screen. The back stack is cleared so Back cannot walk back
    /// into the unlocked app.
    /// </summary>
    [RelayCommand]
    private void Lock() => _navigation.NavigateTo(typeof(LockPage), clearBackStack: true);
}
