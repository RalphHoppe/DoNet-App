using DoNet.Models;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DoNet.Views;

/// <summary>
/// The Persons directory, hosted inside the home screen's content surface.
/// </summary>
public sealed partial class PersonsView : UserControl
{
    public PersonsView()
    {
        // Before InitializeComponent, for the same reason as every other view here:
        // x:Bind resolves its root object while the generated code runs.
        ViewModel = App.Current.Services.GetRequiredService<PersonsViewModel>();

        InitializeComponent();

        Loaded += OnLoaded;
    }

    public PersonsViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        // Fires once, when the control enters the tree. The result is cached in the view
        // model, so switching rail sections does not re-fetch.
        await ViewModel.LoadAsync();
    }

    /// <summary>
    /// Double-click on a card. The full record form does not exist yet, so the command
    /// this reaches is deliberately a no-op - the gesture is wired now so adding that
    /// screen later needs no change here.
    /// </summary>
    private void OnCardDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: PersonPreview person })
        {
            ViewModel.OpenPersonCommand.Execute(person);
        }
    }
}
