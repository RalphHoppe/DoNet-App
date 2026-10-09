using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Controls;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>Which of the three jobs the one record dialog is doing.</summary>
public enum ServiceRecordDialogMode
{
    Preview,
    Add,
    Edit,
}

/// <summary>
/// The labels a relationship field can point at, and the ids behind them.
/// </summary>
/// <param name="Labels">What the picker offers, in store order.</param>
/// <param name="Ids">Label to record id, for saving.</param>
/// <param name="LabelsById">Id to label, for showing what was saved.</param>
public sealed record ServiceChoiceSource(
    IReadOnlyList<string> Labels,
    IReadOnlyDictionary<string, int> Ids,
    IReadOnlyDictionary<int, string> LabelsById)
{
    /// <summary>The label a stored id points at, or empty when it points nowhere.</summary>
    public string LabelFor(int id) => LabelsById.TryGetValue(id, out string? label) ? label : string.Empty;
}

/// <summary>
/// Every relationship kind's options for one type, loaded once when the type is
/// entered and shared by every dialog and card that needs it.
/// </summary>
public sealed class ServiceChoiceOptions
{
    /// <summary>Options by relationship kind.</summary>
    public IReadOnlyDictionary<ServiceFieldKind, ServiceChoiceSource> Sources { get; }

    public ServiceChoiceOptions(
        IReadOnlyDictionary<ServiceFieldKind, ServiceChoiceSource> sources)
    {
        Sources = sources;
    }

    /// <summary>An empty source for a kind nobody loaded.</summary>
    public static ServiceChoiceSource Empty { get; } = new(
        Array.Empty<string>(),
        new Dictionary<string, int>(),
        new Dictionary<int, string>());

    /// <summary>The source for a kind, or the empty one.</summary>
    public ServiceChoiceSource For(ServiceFieldKind kind)
        => Sources.TryGetValue(kind, out ServiceChoiceSource? source) ? source : Empty;
}

/// <summary>
/// One field on the record form: its definition, its editor shape, and its value.
/// </summary>
/// <remarks>
/// The value is what the editor shows - for a relationship that is the *label* of
/// the record it points at, not the id, because a picker speaks labels. The id is
/// resolved when the record is built for the store.
///</remarks>
public sealed partial class RecordFieldEditor : ObservableObject
{
    /// <param name="definition">The field this editor edits.</param>
    /// <param name="value">The starting value: text, or a related record's label.</param>
    public RecordFieldEditor(ServiceFieldDef definition, string value)
    {
        Definition = definition;
        _value = value;
    }

    public ServiceFieldDef Definition { get; }

    [ObservableProperty] private string _value;

    /// <summary>
    /// What the picker offers, when this field is a relationship. The view hands it
    /// to the cell; it is refreshed when a website is created from this form's own
    /// picker, without touching anything typed.
    /// </summary>
    [ObservableProperty] private IReadOnlyList<string>? _options;

    /// <summary>One mark per field, from the kind - the rule the whole app follows.</summary>
    public string IconKind => Definition.Kind switch
    {
        ServiceFieldKind.Text => "Tag",
        ServiceFieldKind.LongText => "FileText",
        ServiceFieldKind.Number => "Hash",
        ServiceFieldKind.Date => "Calendar",
        ServiceFieldKind.Secret => "Lock",
        ServiceFieldKind.Person => "User",
        ServiceFieldKind.Website => "Globe",
        ServiceFieldKind.Account => "Account",
        _ => "Info",
    };

    /// <summary>Which editor the cell shows when the form is editable.</summary>
    public FieldInputKind InputKind => Definition.Kind switch
    {
        ServiceFieldKind.LongText => FieldInputKind.Multiline,
        ServiceFieldKind.Date => FieldInputKind.Date,
        ServiceFieldKind.Person or ServiceFieldKind.Website or ServiceFieldKind.Account
            => FieldInputKind.Choice,
        _ => FieldInputKind.Text,
    };

    public bool IsSecret => Definition.Kind == ServiceFieldKind.Secret;

    public bool IsChoice => Definition.Kind
        is ServiceFieldKind.Person or ServiceFieldKind.Website or ServiceFieldKind.Account;

    /// <summary>
    /// The "add" row label for this field's drawer, or null. Only the website
    /// relationship offers one - the account form's picker is where that flow was
    /// built, and a service record pointing at a site is the same situation.
    /// </summary>
    public string? AddChoiceLabel =>
        Definition.Kind == ServiceFieldKind.Website ? "Add a website" : null;
}

/// <summary>
/// One row of a table on the record form - existing or brand new, collapsed or
/// being edited.
/// </summary>
public sealed partial class RecordRowEditor : ObservableObject
{
    /// <param name="stored">The row as it exists in the store, or null for new.</param>
    /// <param name="fields">The row's field editors, from its stored values.</param>
    public RecordRowEditor(ServiceRecord? stored, IReadOnlyList<RecordFieldEditor> fields)
    {
        Stored = stored;
        Fields = fields;

        // The one-line summary is a view of the fields, so it has to hear what
        // they hear: a keystroke in any cell rewrites the summary beside the row.
        foreach (RecordFieldEditor field in fields)
        {
            field.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RecordFieldEditor.Value))
                {
                    OnPropertyChanged(nameof(Summary));
                }
            };
        }
    }

    public ServiceRecord? Stored { get; }

    public IReadOnlyList<RecordFieldEditor> Fields { get; }

    /// <summary>Whether the row is expanded into its editors.</summary>
    [ObservableProperty] private bool _isEditing;

    /// <summary>The row as one line: the first two filled values.</summary>
    public string Summary
    {
        get
        {
            string[] filled = Fields
                .Select(f => f.Value.Trim())
                .Where(v => v.Length > 0)
                .Take(2)
                .ToArray();

            return filled.Length == 0 ? "Empty row" : string.Join(" \u00B7 ", filled);
        }
    }
}

/// <summary>One table on the record form: its definition and its rows.</summary>
public sealed partial class RecordTableEditor : ObservableObject
{
    public RecordTableEditor(ServiceTableDef definition)
    {
        Definition = definition;
    }

    public ServiceTableDef Definition { get; }

    public ObservableCollection<RecordRowEditor> Rows { get; } = new();
}

/// <summary>
/// Backs the record dialog for a designed service type, in all three of its modes.
/// </summary>
/// <remarks>
/// The form is built from the type's structure rather than laid out in XAML,
/// because the structure is data: the fields the user designed, in their order,
/// each as the FieldCell its kind calls for. The dialog's XAML is the shell - the
/// header, the scroller, the footer - and the fields arrive as editors this view
/// model hands to the code behind.
///</remarks>
public sealed partial class ServiceRecordDialogViewModel : ObservableObject
{
    private ServiceRecord? _original;
    private ServiceDefinition? _definition;
    private ServiceChoiceOptions _choices = new(
        new Dictionary<ServiceFieldKind, ServiceChoiceSource>());

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(IsPreview))]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(FieldKind))]
    private ServiceRecordDialogMode _mode = ServiceRecordDialogMode.Preview;

    [ObservableProperty] private string _typeName = string.Empty;
    [ObservableProperty] private string _idDisplay = string.Empty;
    [ObservableProperty] private string _createdAtDisplay = string.Empty;

    public List<RecordFieldEditor> Fields { get; } = new();

    public List<RecordTableEditor> Tables { get; } = new();

    public string Title => Mode switch
    {
        ServiceRecordDialogMode.Add => $"ADD {TypeName.ToUpperInvariant()}",
        ServiceRecordDialogMode.Edit => $"EDIT {TypeName.ToUpperInvariant()}",
        _ => $"{TypeName.ToUpperInvariant()} INFO",
    };

    public string Subtitle => Mode switch
    {
        ServiceRecordDialogMode.Add => $"Create a {TypeName.ToLowerInvariant()} record.",
        ServiceRecordDialogMode.Edit => "Edit this record \u00B7 # cannot be changed.",
        _ => $"Full {TypeName.ToLowerInvariant()} record",
    };

    public string PrimaryActionText => Mode switch
    {
        ServiceRecordDialogMode.Add => $"ADD {TypeName.ToUpperInvariant()}",
        ServiceRecordDialogMode.Edit => "SAVE CHANGES",
        _ => "EDIT RECORD",
    };

    public bool IsPreview => Mode == ServiceRecordDialogMode.Preview;

    public bool IsEditing => Mode != ServiceRecordDialogMode.Preview;

    /// <summary>Display or editable, for every cell on the dialog at once.</summary>
    public FieldCellKind FieldKind =>
        IsPreview ? FieldCellKind.Display : FieldCellKind.Editable;

    /// <summary>
    /// True when the form has enough to save: the name field, when the type has
    /// one, must be filled.
    /// </summary>
    public bool CanSave => _definition?.NameField is not { } name
        || Fields.FirstOrDefault(f => f.Definition.Key == name.Key) is not { } editor
        || !string.IsNullOrWhiteSpace(editor.Value);

    /// <summary>The definition this form was built from.</summary>
    public ServiceDefinition? Definition => _definition;

    /// <summary>The record the form was opened on, or null when adding.</summary>
    public ServiceRecord? Original => _original;

    /// <summary>The relationship options the pickers offer.</summary>
    public ServiceChoiceOptions Choices => _choices;

    public void ShowPreview(
        ServiceRecord record,
        IReadOnlyList<ServiceRecord> rows,
        ServiceDefinition definition,
        string typeName,
        ServiceChoiceOptions choices)
    {
        _original = record;
        Fill(record, rows, definition, typeName, choices);
        Mode = ServiceRecordDialogMode.Preview;
    }

    public void ShowAdd(
        ServiceDefinition definition, string typeName, ServiceChoiceOptions choices)
    {
        _original = null;
        Fill(new ServiceRecord(), Array.Empty<ServiceRecord>(), definition, typeName, choices);
        IdDisplay = string.Empty;
        CreatedAtDisplay = string.Empty;
        Mode = ServiceRecordDialogMode.Add;
    }

    public void ShowEdit(
        ServiceRecord record,
        IReadOnlyList<ServiceRecord> rows,
        ServiceDefinition definition,
        string typeName,
        ServiceChoiceOptions choices)
    {
        _original = record;
        Fill(record, rows, definition, typeName, choices);
        Mode = ServiceRecordDialogMode.Edit;
    }

    /// <summary>Preview turns into edit in place.</summary>
    public void SwitchToEdit() => Mode = ServiceRecordDialogMode.Edit;

    /// <summary>
    /// Re-evaluates <see cref="CanSave"/>. The view calls it when a committed
    /// value has landed in an editor, because the editors are the form and the
    /// save button is the only thing that watches them.
    /// </summary>
    public void Touch() => OnPropertyChanged(nameof(CanSave));

    /// <summary>
    /// Starts a new row in one of the form's tables, already expanded into its
    /// editors - a row that opens collapsed is a button that has to be pressed
    /// twice.
    /// </summary>
    public void AddRow(RecordTableEditor table)
    {
        RecordRowEditor row = new(
            null,
            table.Definition.Fields
                .Select(f => new RecordFieldEditor(f, string.Empty) { Options = OptionsFor(f) })
                .ToList());

        row.IsEditing = true;
        table.Rows.Add(row);
    }

    private IReadOnlyList<string>? OptionsFor(ServiceFieldDef field)
        => field.Kind is ServiceFieldKind.Person
            or ServiceFieldKind.Website
            or ServiceFieldKind.Account
            ? _choices.For(field.Kind).Labels
            : null;

    /// <summary>
    /// Refreshes only the relationship options, without touching anything typed.
    /// </summary>
    /// <remarks>
    /// Called when a website is created from one of this form's own pickers: the
    /// pickers need the new option, the rest of the form is the user's work in
    /// progress and must survive.
    /// </remarks>
    public void RefreshChoices(ServiceChoiceOptions choices)
    {
        _choices = choices;

        // Every picker's list, re-pointed at the new options. The values stay as
        // typed - refreshing what can be picked is not an excuse to move what was.
        foreach (RecordFieldEditor editor in Fields.Concat(AllRowEditors()))
        {
            if (editor.IsChoice)
            {
                editor.Options = _choices.For(editor.Definition.Kind).Labels;
            }
        }

        // The view rebuilds its cells on this signal, so they pick up the new
        // options without losing anything typed.
        OnPropertyChanged(nameof(Choices));
    }

    private IEnumerable<RecordFieldEditor> AllRowEditors()
        => Tables.SelectMany(t => t.Rows.SelectMany(r => r.Fields));

    /// <summary>
    /// The form as a record plus its table rows, ready for the store.
    /// </summary>
    /// <param name="record">The top-level record, with data and search text set.</param>
    /// <param name="rows">The table rows, in definition order.</param>
    public void ToRecord(out ServiceRecord record, out List<ServiceRecord> rows)
    {
        ServiceRecord build = _original?.Clone() ?? new ServiceRecord();

        Dictionary<string, string> data = new();

        foreach (RecordFieldEditor editor in Fields)
        {
            string value = editor.Value.Trim();

            if (editor.IsChoice)
            {
                // Labels in, ids out. An unresolvable label - typed but never
                // picked, or pointing at something deleted - stores nothing rather
                // than a broken reference.
                value = _choices.For(editor.Definition.Kind)
                    .Ids.TryGetValue(value, out int id)
                    ? id.ToString()
                    : string.Empty;
            }

            data[editor.Definition.Key] = value;
        }

        build.Title = TitleFrom(Fields);
        build.DataJson = ServiceRecord.PackData(data);
        build.SearchText = SearchTextFrom(Fields);
        build.TableKey = string.Empty;
        build.ParentRecordId = null;

        rows = new List<ServiceRecord>();

        foreach (RecordTableEditor table in Tables)
        {
            foreach (RecordRowEditor row in table.Rows)
            {
                // A row with nothing in it is nothing to store; saving would fill
                // the type with empty rows the user opened and closed.
                if (row.Fields.All(f => string.IsNullOrWhiteSpace(f.Value)))
                {
                    continue;
                }

                Dictionary<string, string> rowData = new();

                foreach (RecordFieldEditor editor in row.Fields)
                {
                    string value = editor.Value.Trim();

                    if (editor.IsChoice)
                    {
                        value = _choices.For(editor.Definition.Kind)
                            .Ids.TryGetValue(value, out int id)
                            ? id.ToString()
                            : string.Empty;
                    }

                    rowData[editor.Definition.Key] = value;
                }

                rows.Add(new ServiceRecord
                {
                    ServiceTypeId = build.ServiceTypeId,
                    TableKey = table.Definition.Key,
                    Title = TitleFrom(row.Fields),
                    DataJson = ServiceRecord.PackData(rowData),
                    SearchText = SearchTextFrom(row.Fields),
                });
            }
        }

        record = build;
    }

    /// <summary>
    /// Wires an editor into the save state: the value is the only thing the form
    /// insists on (through the name field), so every keystroke is worth a
    /// re-evaluation.
    /// </summary>
    private RecordFieldEditor Track(RecordFieldEditor editor)
    {
        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RecordFieldEditor.Value))
            {
                OnPropertyChanged(nameof(CanSave));
            }
        };

        return editor;
    }

    private void Fill(
        ServiceRecord record,
        IReadOnlyList<ServiceRecord> storedRows,
        ServiceDefinition definition,
        string typeName,
        ServiceChoiceOptions choices)
    {
        _definition = definition;
        _choices = choices;
        TypeName = typeName;

        Fields.Clear();
        Tables.Clear();

        foreach (ServiceFieldDef field in definition.Fields)
        {
            string value = record.Value(field.Key);

            if (field.Kind is ServiceFieldKind.Person
                or ServiceFieldKind.Website
                or ServiceFieldKind.Account)
            {
                value = int.TryParse(value, out int id)
                    ? LabelFor(choices, field.Kind, id)
                    : string.Empty;
            }

            Fields.Add(Track(new RecordFieldEditor(field, value)
            {
                Options = OptionsFor(field),
            }));
        }

        foreach (ServiceTableDef table in definition.Tables)
        {
            RecordTableEditor editor = new(table);

            foreach (ServiceRecord stored in storedRows.Where(r => r.TableKey == table.Key))
            {
                editor.Rows.Add(new RecordRowEditor(
                    stored,
                    table.Fields
                        .Select(f => Track(new RecordFieldEditor(f, ValueFor(stored, choices, f))
                        {
                            Options = OptionsFor(f),
                        }))
                        .ToList()));
            }

            Tables.Add(editor);
        }

        IdDisplay = record.Id > 0 ? record.IdDisplay : string.Empty;
        CreatedAtDisplay = record.CreatedAtDisplay;
    }

    private static string ValueFor(
        ServiceRecord stored, ServiceChoiceOptions choices, ServiceFieldDef field)
    {
        string value = stored.Value(field.Key);

        if (field.Kind is ServiceFieldKind.Person
            or ServiceFieldKind.Website
            or ServiceFieldKind.Account)
        {
            return int.TryParse(value, out int id)
                ? LabelFor(choices, field.Kind, id)
                : string.Empty;
        }

        return value;
    }

    private static string LabelFor(
        ServiceChoiceOptions choices, ServiceFieldKind kind, int id)
        => choices.For(kind).LabelFor(id);

    private static string TitleFrom(IEnumerable<RecordFieldEditor> fields)
    {
        foreach (RecordFieldEditor editor in fields)
        {
            if (editor.Definition.Kind == ServiceFieldKind.Text
                && !string.IsNullOrWhiteSpace(editor.Value))
            {
                return editor.Value.Trim();
            }
        }

        return string.Empty;
    }

    private static string SearchTextFrom(IEnumerable<RecordFieldEditor> fields)
        => string.Join(
            " ",
            fields
                .Where(f => f.Definition.Kind != ServiceFieldKind.Secret)
                .Select(f => f.Value.Trim())
                .Where(v => v.Length > 0));
}
