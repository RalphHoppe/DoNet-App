using System;
using System.ComponentModel;
using DoNet.Controls;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;
using DoNet.ViewModels;
using DoNet.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System.Collections.Specialized;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The modal person dialog. See the XAML for why one control serves all three modes.
/// </summary>
public sealed partial class WebsiteDialog : UserControl
{
    private readonly WebsitesViewModel _host;

    public WebsiteDialog()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind binds
        // its root object while the generated code runs.
        _host = App.Current.Services.GetRequiredService<WebsitesViewModel>();
        ViewModel = _host.Dialog;

        InitializeComponent();

        _host.PropertyChanged += OnHostPropertyChanged;
        ViewModel.PropertyChanged += OnDialogPropertyChanged;

        // The chips mirror the selection, and the dismiss crosses only exist while
        // editing - so both the collection and the mode have to redraw them.
        ViewModel.Selected.CollectionChanged += (_, _) => RebuildChips();

        // A deferred control can be created *because* it is already supposed to be
        // showing, in which case the property change that opens it fired before this
        // instance existed. Catch up once we are in the tree - Open touches the visual
        // tree and starts a storyboard, so the constructor is too early.
        Loaded += (_, _) =>
        {
            if (_host.IsDialogOpen && Root.Visibility != Visibility.Visible)
            {
                Open();
            }
        };

        // HomePage is rebuilt on every unlock, and the view model is a singleton, so
        // without this each lock/unlock cycle leaves another detached dialog listening
        // to it. They all react, all try to animate, and the ones no longer in the
        // visual tree throw while doing it.
        Unloaded += (_, _) =>
        {
            _host.PropertyChanged -= OnHostPropertyChanged;
            ViewModel.PropertyChanged -= OnDialogPropertyChanged;
        };
    }

    public WebsiteDialogViewModel ViewModel { get; }



    /// <summary>Animates the switch between preview and edit.</summary>
    private void OnDialogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Only when the dialog is already up. Opening runs its own entrance, and
        // playing both at once makes the panel visibly stutter.
        if (args.PropertyName == nameof(WebsiteDialogViewModel.Mode))
        {
            RebuildChips();
        }

        if (args.PropertyName == nameof(WebsiteDialogViewModel.Mode)
            && Root.Visibility == Visibility.Visible
            && Resources["ModeChangeStoryboard"] is Storyboard change)
        {
            change.Begin();
        }
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(WebsitesViewModel.IsDialogOpen))
        {
            if (_host.IsDialogOpen)
            {
                Open();
            }
            else if (Root.Visibility == Visibility.Visible)
            {
                Close();
            }
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

    /// <summary>
    /// Plays the exit, then hides. Hiding only after the animation completes is the
    /// whole point - collapsing first would make the close instant and the storyboard
    /// invisible.
    /// </summary>
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
    private void OnScrimTapped(object sender, TappedRoutedEventArgs args) => _host.CloseDialog();

    private void OnCloseClick(object sender, RoutedEventArgs args) => _host.CloseDialog();

    private void OnCancelClick(object sender, RoutedEventArgs args) => _host.CloseDialog();

    /// <remarks>
    /// An async void handler that throws takes the process down - there is no caller
    /// to catch it and the framework has nowhere to send it. Every one of them is
    /// wrapped.
    /// </remarks>
    private async void OnPrimaryClick(object sender, RoutedEventArgs args)
    {
        try
        {
            await _host.CommitDialogAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Saving a website failed", error);
        }
    }

    /// <summary>
    /// Releases this control's compiled bindings. Called when the host page is leaving
    /// for good; see <see cref="HomePage.OnNavigatedFrom"/> for why it is not automatic.
    /// </summary>
    public void ReleaseBindings() => Bindings.StopTracking();


    /// <summary>
    /// Rebuilds the chips whenever the selection changes.
    /// </summary>
    /// <remarks>
    /// Built in code into a WrapPanel rather than bound to an ItemsRepeater, because
    /// chips are as wide as their text and ItemsRepeater has no wrapping layout that
    /// allows variable widths. There are only ever a handful.
    ///
    /// In preview the chips are read-only; while editing each carries a dismiss cross,
    /// which unticks the matching box - the list and the ticks are two views of one
    /// selection, so removing from either has to update both.
    /// </remarks>
    private void RebuildChips()
    {
        SelectedChips.Children.Clear();

        bool any = ViewModel.Selected.Count > 0;

        SelectedChips.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

        // The "none selected" line is only worth saying while reading. In the form the
        // empty tick boxes already say it.
        NoMethodsText.Visibility = !any && ViewModel.IsPreview
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!any)
        {
            return;
        }

        foreach (string method in ViewModel.Selected)
        {
            SelectedChips.Children.Add(BuildChip(method, ViewModel.IsEditing));
        }
    }

    private Border BuildChip(string method, bool removable)
    {
        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };

        content.Children.Add(new Controls.PaymentMethodIcon
        {
            Method = method,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
        });

        content.Children.Add(new TextBlock
        {
            Text = method,
            FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
            FontSize = 13,
            Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (removable)
        {
            Button dismiss = new()
            {
                Style = (Style)Application.Current.Resources["BareButtonStyle"],
                Width = 18,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock
                {
                    Text = "\u2715",
                    FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
                    FontSize = 11,
                    Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            string captured = method;
            dismiss.Click += (_, _) => ViewModel.Deselect(captured);

            content.Children.Add(dismiss);
        }

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(10, 4, removable ? 6 : 12, 4),
            Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
            BorderBrush = (Brush)Application.Current.Resources["AddActionBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private void OnAddMethodClick(object sender, RoutedEventArgs args) => CommitNewMethod();

    /// <summary>Enter adds the method, so the button is not the only way.</summary>
    private void OnNewMethodKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Enter)
        {
            return;
        }

        args.Handled = true;
        CommitNewMethod();
    }

    private void CommitNewMethod()
    {
        try
        {
            if (ViewModel.CommitNewMethod() is { } added)
            {
                // Only genuinely new methods are worth remembering; the view model
                // returns null when the box was blank or the method already existed.
                _host.RememberMethod(added);
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Adding a payment method failed", error);
        }
    }
}
