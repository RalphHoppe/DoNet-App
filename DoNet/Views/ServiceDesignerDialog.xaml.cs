using System;
using System.ComponentModel;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The structure designer modal. See the XAML for the shape it offers.
/// </summary>
public sealed partial class ServiceDesignerDialog : UserControl
{
    private readonly ServicesViewModel _host;

    public ServiceDesignerDialog()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind binds
        // its root object while the generated code runs.
        _host = App.Current.Services.GetRequiredService<ServicesViewModel>();
        ViewModel = _host.Designer;

        InitializeComponent();

        _host.PropertyChanged += OnHostPropertyChanged;

        // A deferred control can be created *because* it is already supposed to be
        // showing; catch up once we are in the tree, as the other dialogs do.
        Loaded += (_, _) =>
        {
            if (_host.IsDesignerOpen && Root.Visibility != Visibility.Visible)
            {
                Open();
            }
        };

        // HomePage is rebuilt on every unlock, and the view model is a singleton -
        // without this, each cycle leaves another detached listener behind.
        Unloaded += (_, _) => Detach();
    }

    public ServiceDesignerViewModel ViewModel { get; }

    /// <summary>
    /// Releases this control's hold on the view model. See the other dialogs for
    /// why this is not automatic.
    /// </summary>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        Detach();
    }

    private void Detach() => _host.PropertyChanged -= OnHostPropertyChanged;

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ServicesViewModel.IsDesignerOpen))
        {
            return;
        }

        if (_host.IsDesignerOpen)
        {
            Open();
        }
        else if (Root.Visibility == Visibility.Visible)
        {
            Close();
        }
    }

    private void Open()
    {
        Root.Visibility = Visibility.Visible;

        if (Resources["OpenStoryboard"] is Storyboard open)
        {
            open.Begin();
        }
    }

    private void Close()
    {
        if (Resources["CloseStoryboard"] is Storyboard close)
        {
            close.Begin();
        }
        else
        {
            Root.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCloseCompleted(object? sender, object args)
    {
        Root.Visibility = Visibility.Collapsed;

        // Reset the transform the exit left behind, or the next open starts displaced.
        CardOffset.Y = 0;
        Card.Opacity = 1;
        Scrim.Opacity = 1;
    }

    /// <summary>Click-away on the backdrop closes, as a modal should.</summary>
    private void OnScrimTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
        => _host.IsDesignerOpen = false;

    private void OnCloseClick(object sender, RoutedEventArgs args) => _host.IsDesignerOpen = false;

    private void OnCancelClick(object sender, RoutedEventArgs args) => _host.IsDesignerOpen = false;

    /// <summary>
    /// Saves the structure. The commit itself lives on the host, because only the
    /// host knows whether this ends in the type's records (first open) or over
    /// them (a later edit).
    /// </summary>
    private async void OnSaveClick(object sender, RoutedEventArgs args)
    {
        try
        {
            await _host.SaveStructureAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Saving a service structure failed", error);
        }
    }

    private void OnAddFieldClick(object sender, RoutedEventArgs args) => ViewModel.AddField();

    private void OnAddTableClick(object sender, RoutedEventArgs args) => ViewModel.AddTable();

    /// <summary>
    /// The row buttons carry their row as the CommandParameter, because the
    /// template binds the row - the dialog knows the list it belongs to.
    /// </summary>
    private void OnMoveUpClick(object sender, RoutedEventArgs args)
    {
        if (Parameter<DesignerFieldDraft>(sender) is { } field)
        {
            ViewModel.MoveField(field, -1);
        }
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs args)
    {
        if (Parameter<DesignerFieldDraft>(sender) is { } field)
        {
            ViewModel.MoveField(field, 1);
        }
    }

    private void OnRemoveClick(object sender, RoutedEventArgs args)
    {
        if (Parameter<DesignerFieldDraft>(sender) is { } field)
        {
            ViewModel.RemoveField(field);
        }
    }

    private void OnRemoveTableClick(object sender, RoutedEventArgs args)
    {
        if (Parameter<DesignerTableDraft>(sender) is { } table)
        {
            ViewModel.RemoveTable(table);
        }
    }

    private void OnAddTableFieldClick(object sender, RoutedEventArgs args)
    {
        if (Parameter<DesignerTableDraft>(sender) is { } table)
        {
            ViewModel.AddTableField(table);
        }
    }

    /// <summary>The row a template button was pressed on, if it still has one.</summary>
    private static T? Parameter<T>(object sender)
        where T : class
        => sender is Button { CommandParameter: T parameter } ? parameter : null;
}
