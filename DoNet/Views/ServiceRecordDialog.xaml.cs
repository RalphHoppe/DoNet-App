using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using DoNet.Controls;
using DoNet.Models;
using DoNet.Services;
using DoNet.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace DoNet.Views;

/// <summary>
/// The record dialog for a designed service type. See the XAML for why the form
/// is built in code.
/// </summary>
/// <remarks>
/// The XAML is the shell; this code-behind builds the form from the view model's
/// editors - the fields the user designed for the type, each as the FieldCell its
/// kind calls for - and rebuilds it when the mode, the options or the table rows
/// change. The values live in the editors, so a rebuild never loses anything
/// typed.
///</remarks>
public sealed partial class ServiceRecordDialog : UserControl
{
    private readonly ServicesViewModel _host;
    private readonly WebsitesViewModel _websites;

    /// <summary>
    /// The picker the user left to create a website, or null. When that dialog
    /// closes, this is the editor its new site lands in.
    /// </summary>
    private RecordFieldEditor? _awaitingWebsiteEditor;

    /// <summary>Whether the form's row subscriptions are attached.</summary>
    private bool _wired;

    private int _tabOrder;

    public ServiceRecordDialog()
    {
        // Resolved before InitializeComponent, as everywhere else here: x:Bind binds
        // its root object while the generated code runs.
        _host = App.Current.Services.GetRequiredService<ServicesViewModel>();
        ViewModel = _host.RecordDialog;
        _websites = App.Current.Services.GetRequiredService<WebsitesViewModel>();

        InitializeComponent();

        _host.PropertyChanged += OnHostPropertyChanged;
        ViewModel.PropertyChanged += OnDialogPropertyChanged;
        _websites.PropertyChanged += OnWebsitesPropertyChanged;

        // A deferred control can be created *because* it is already supposed to be
        // showing; catch up once we are in the tree, as the other dialogs do.
        Loaded += (_, _) =>
        {
            if (_host.IsRecordDialogOpen && Root.Visibility != Visibility.Visible)
            {
                Open();
            }
        };

        // HomePage is rebuilt on every unlock, and the view models are singletons -
        // without this, each cycle leaves another detached listener behind.
        Unloaded += (_, _) => Detach();
    }

    public ServiceRecordDialogViewModel ViewModel { get; }

    /// <summary>
    /// Releases this control's hold on the view models. See the other dialogs for
    /// why this is not automatic.
    /// </summary>
    public void ReleaseBindings()
    {
        Bindings.StopTracking();
        Detach();
    }

    private void Detach()
    {
        _host.PropertyChanged -= OnHostPropertyChanged;
        ViewModel.PropertyChanged -= OnDialogPropertyChanged;
        _websites.PropertyChanged -= OnWebsitesPropertyChanged;
        UnwireForm();
    }

    private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ServicesViewModel.IsRecordDialogOpen))
        {
            return;
        }

        if (_host.IsRecordDialogOpen)
        {
            Open();
        }
        else if (Root.Visibility == Visibility.Visible)
        {
            Close();
        }
    }

    /// <summary>
    /// The form reacts to its own mode and options changing, not to the values -
    /// those flow through the cells and the editors without a rebuild.
    /// </summary>
    private void OnDialogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (Root.Visibility != Visibility.Visible)
        {
            return;
        }

        if (args.PropertyName == nameof(ServiceRecordDialogViewModel.Mode))
        {
            BuildForm();

            if (Resources["ModeChangeStoryboard"] is Storyboard change)
            {
                change.Begin();
            }
        }
        else if (args.PropertyName == nameof(ServiceRecordDialogViewModel.Choices))
        {
            BuildForm();
        }
    }

    /// <summary>
    /// Brings a website created from one of this form's pickers back into the
    /// picker it was created for.
    /// </summary>
    private void OnWebsitesPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(WebsitesViewModel.IsDialogOpen)
            || _websites.IsDialogOpen
            || _awaitingWebsiteEditor is not { } editor
            || _websites.LastAddedWebsite is not { } added)
        {
            return;
        }

        editor.Value = added.PickerLabel;
        _awaitingWebsiteEditor = null;
    }

    private void Open()
    {
        Root.Visibility = Visibility.Visible;

        BuildForm();
        WireForm();

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
            OnClosed();
        }
    }

    private void OnCloseCompleted(object? sender, object args) => OnClosed();

    private void OnClosed()
    {
        Root.Visibility = Visibility.Collapsed;
        UnwireForm();
        _awaitingWebsiteEditor = null;

        // Reset the transform the exit left behind, or the next open starts displaced.
        CardOffset.Y = 0;
        Card.Opacity = 1;
        Scrim.Opacity = 1;
    }

    /// <summary>Click-away on the backdrop closes, as a modal should.</summary>
    private void OnScrimTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs args)
        => _host.CloseRecordDialog();

    private void OnCloseClick(object sender, RoutedEventArgs args) => _host.CloseRecordDialog();

    private void OnCancelClick(object sender, RoutedEventArgs args) => _host.CloseRecordDialog();

    /// <remarks>
    /// An async void handler that throws takes the process down; every one of them
    /// is wrapped.
    /// </remarks>
    private async void OnPrimaryClick(object sender, RoutedEventArgs args)
    {
        try
        {
            await _host.CommitRecordDialogAsync();
        }
        catch (Exception error)
        {
            AppLog.Error("Saving a service record failed", error);
        }
    }

    // ------------------------------------------------------------------
    // The form
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the whole form from the editors. Safe to run again; the values live
    /// in the editors, and the cells are views of them.
    /// </summary>
    private void BuildForm()
    {
        _tabOrder = 1;

        FieldsHost.Children.Clear();

        foreach (RecordFieldEditor editor in ViewModel.Fields)
        {
            FieldsHost.Children.Add(BuildCell(editor));
        }

        TablesHost.Children.Clear();
        TablesHost.Visibility = ViewModel.Tables.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        foreach (RecordTableEditor table in ViewModel.Tables)
        {
            TablesHost.Children.Add(BuildTable(table));
        }

        IdCell.TabOrder = _tabOrder++;
        CreatedCell.TabOrder = _tabOrder++;
    }

    /// <summary>One field of the record, as the FieldCell its kind calls for.</summary>
    private FieldCell BuildCell(RecordFieldEditor editor)
    {
        string label = editor.Definition.Label;

        FieldCell cell = new()
        {
            Label = label.ToUpperInvariant(),
            IconKind = editor.IconKind,
            Placeholder = $"Enter {label.ToLowerInvariant()}",
            TabOrder = _tabOrder++,
            InputKind = editor.InputKind,
            IsSecret = editor.IsSecret,
            ShowCopy = ViewModel.IsPreview,
            ShowGenerate = editor.IsSecret,
            Kind = ViewModel.FieldKind,
            Value = editor.Value,
            Options = editor.Options,
        };

        if (editor.AddChoiceLabel is string addLabel)
        {
            cell.AddChoiceLabel = addLabel;
            cell.AddChoiceRequested += (_, _) => OnAddChoiceRequested(editor);
        }

        cell.ValueCommitted += (_, value) => editor.Value = value;

        return cell;
    }

    /// <summary>
    /// Opens the website dialog for one of this form's pickers, and remembers
    /// which one - the new site comes back to it.
    /// </summary>
    private void OnAddChoiceRequested(RecordFieldEditor editor)
    {
        _awaitingWebsiteEditor = editor;
        _websites.AddWebsite();
    }

    /// <summary>One table: its name, its rows, and its add-row button.</summary>
    private StackPanel BuildTable(RecordTableEditor table)
    {
        StackPanel block = new() { Spacing = 12 };

        block.Children.Add(new TextBlock
        {
            Text = table.Definition.Label.ToUpperInvariant(),
            FontFamily = (FontFamily)Application.Current.Resources["BaumansFont"],
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
            CharacterSpacing = 20,
        });

        foreach (RecordRowEditor row in table.Rows)
        {
            block.Children.Add(BuildRow(table, row));
        }

        if (!ViewModel.IsPreview)
        {
            block.Children.Add(BuildAddRowButton(table));
        }

        return block;
    }

    /// <summary>
    /// One row of a table: collapsed to its summary while editing existing rows,
    /// expanded into its editors when new or opened.
    /// </summary>
    private Border BuildRow(RecordTableEditor table, RecordRowEditor row)
    {
        // The expanded face: the row's own fields.
        StackPanel editors = new() { Spacing = 12 };

        foreach (RecordFieldEditor field in row.Fields)
        {
            editors.Children.Add(BuildCell(field));
        }

        // The collapsed face: the row as one line, with open and remove.
        TextBlock summary = new()
        {
            Text = row.Summary,
            FontFamily = (FontFamily)Application.Current.Resources["BalooFont"],
            FontSize = (double)Application.Current.Resources["FieldValueFontSize"],
            Foreground = (Brush)Application.Current.Resources["TextPrimaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        Grid face = new() { ColumnSpacing = 10 };
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        face.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

        Grid.SetColumn(summary, 0);
        face.Children.Add(summary);

        Button open = BuildRowButton("Open row", "Pencil", () => row.IsEditing = true);
        Grid.SetColumn(open, 1);
        face.Children.Add(open);

        Button remove = BuildRowButton("Remove row", "Trash", () => table.Rows.Remove(row));
        Grid.SetColumn(remove, 2);
        face.Children.Add(remove);

        Border faceCard = new()
        {
            Padding = new Thickness(14, 8, 14, 8),
            CornerRadius = new CornerRadius(16),
            Background = (Brush)Application.Current.Resources["CardAvatarBrush"],
            BorderBrush = (Brush)Application.Current.Resources["FieldBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = face,
        };

        bool expanded = ViewModel.IsPreview || row.IsEditing;
        faceCard.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        editors.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        StackPanel both = new() { Spacing = 12 };
        both.Children.Add(faceCard);
        both.Children.Add(editors);

        return new Border { Child = both };
    }

    /// <summary>One small ghost button for a collapsed row.</summary>
    private Button BuildRowButton(string tooltip, string icon, Action click)
    {
        Button button = new()
        {
            Width = 32,
            Height = 32,
            Style = (Style)Application.Current.Resources["BareButtonStyle"],
            Content = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(16),
                Background = (Brush)Application.Current.Resources["CardBackgroundBrush"],
                BorderBrush = (Brush)Application.Current.Resources["FieldBorderBrush"],
                BorderThickness = new Thickness(1),
                Child = new LineIcon
                {
                    Kind = icon,
                    Width = 14,
                    Height = 14,
                    Foreground = (Brush)Application.Current.Resources["CardMetaBrush"],
                },
            },
        };

        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => click();

        return button;
    }

    /// <summary>The ghost pill that starts a new row in a table.</summary>
    private Button BuildAddRowButton(RecordTableEditor table)
    {
        TextBlock text = new()
        {
            Text = "ADD ROW",
            FontFamily = (FontFamily)Application.Current.Resources["JerseyFont"],
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
            CharacterSpacing = 40,
            VerticalAlignment = VerticalAlignment.Center,
        };

        LineIcon plus = new()
        {
            Kind = "Plus",
            Width = 14,
            Height = 14,
            Foreground = (Brush)Application.Current.Resources["BrandPrimaryBrush"],
        };

        StackPanel stack = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        stack.Children.Add(plus);
        stack.Children.Add(text);

        Border pill = new()
        {
            Height = 34,
            Padding = new Thickness(12, 0, 16, 0),
            CornerRadius = new CornerRadius(14),
            Background = (Brush)Application.Current.Resources["CardAvatarBrush"],
            BorderBrush = (Brush)Application.Current.Resources["FieldBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = stack,
        };

        Button button = new()
        {
            Style = (Style)Application.Current.Resources["BareButtonStyle"],
            Content = pill,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        button.Click += (_, _) => ViewModel.AddRow(table);

        return button;
    }

    // ------------------------------------------------------------------
    // Row change plumbing
    // ------------------------------------------------------------------

    /// <summary>
    /// Attaches the form's row subscriptions. Called on open; BuildForm itself
    /// subscribes to nothing, so rebuilding cannot duplicate a listener.
    /// </summary>
    private void WireForm()
    {
        if (_wired)
        {
            return;
        }

        _wired = true;

        foreach (RecordTableEditor table in ViewModel.Tables)
        {
            table.Rows.CollectionChanged += OnRowsChanged;

            foreach (RecordRowEditor row in table.Rows)
            {
                row.PropertyChanged += OnRowChanged;
            }
        }
    }

    private void UnwireForm()
    {
        if (!_wired)
        {
            return;
        }

        _wired = false;

        foreach (RecordTableEditor table in ViewModel.Tables)
        {
            table.Rows.CollectionChanged -= OnRowsChanged;

            foreach (RecordRowEditor row in table.Rows)
            {
                row.PropertyChanged -= OnRowChanged;
            }
        }
    }

    /// <summary>
    /// A row arriving or leaving rewrites its table: the form is rebuilt, and the
    /// arriving row is subscribed so its own open/close can rewrite it again.
    /// </summary>
    private void OnRowsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.NewItems is not null)
        {
            foreach (object item in args.NewItems)
            {
                if (item is RecordRowEditor row)
                {
                    row.PropertyChanged += OnRowChanged;
                }
            }
        }

        if (args.OldItems is not null)
        {
            foreach (object item in args.OldItems)
            {
                if (item is RecordRowEditor row)
                {
                    row.PropertyChanged -= OnRowChanged;
                }
            }
        }

        BuildForm();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RecordRowEditor.IsEditing))
        {
            BuildForm();
        }
    }
}
