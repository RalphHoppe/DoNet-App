using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>
/// One field row in the structure designer.
/// </summary>
/// <remarks>
/// Observable because the rows are live: typing a label or changing the kind is
/// editing the structure, and the save button has to know the moment it becomes
/// worth pressing.
/// </remarks>
public sealed partial class DesignerFieldDraft : ObservableObject
{
    /// <param name="key">The stable key; see <see cref="ServiceFieldDef.Key"/>.</param>
    /// <param name="label">The label as typed.</param>
    /// <param name="kindName">The kind as the picker shows it.</param>
    public DesignerFieldDraft(string key, string label, string kindName)
    {
        Key = key;
        _label = label;
        _kindName = kindName;
    }

    /// <summary>The stable identity of the field. Assigned at creation, never changed.</summary>
    public string Key { get; }

    [ObservableProperty] private string _label;

    [ObservableProperty] private string _kindName;

    /// <summary>Whether this row names every record - the first text field is.</summary>
    public bool IsName => KindName == KindNames[0];

    /// <summary>
    /// Raised when the label or the kind changed, for the designer to re-evaluate
    /// its save button with.
    /// </summary>
    public event EventHandler? Changed;

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);

        if (args.PropertyName is nameof(Label) or nameof(KindName))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>One table block in the structure designer, with its own field rows.</summary>
public sealed partial class DesignerTableDraft : ObservableObject
{
    /// <param name="key">The stable key; see <see cref="ServiceTableDef.Key"/>.</param>
    /// <param name="label">The table's name as typed.</param>
    public DesignerTableDraft(string key, string label)
    {
        Key = key;
        _label = label;
    }

    /// <summary>The stable identity of the table. Assigned at creation, never changed.</summary>
    public string Key { get; }

    [ObservableProperty] private string _label;

    public ObservableCollection<DesignerFieldDraft> Fields { get; } = new();

    /// <summary>Raised when the label changed, for the designer's save button.</summary>
    public event EventHandler? Changed;

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);

        if (args.PropertyName == nameof(Label))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// Backs the structure designer: the window where a service type is given its
/// fields, its relationships and its tables.
/// </summary>
/// <remarks>
/// This is the screen the simulation workbook could not describe and the user
/// should not have to: instead of tables and columns, it offers the three things
/// a record actually needs - what every one carries, which other records it can
/// point at, and what lists ride along with it. The kinds are deliberately few
/// and deliberately nouns rather than database words: "Password", not "nvarchar";
/// "Person", not "foreign key".
///</remarks>
public sealed partial class ServiceDesignerViewModel : ObservableObject
{
    /// <summary>The kinds a field can be, as the picker shows them, in picker order.</summary>
    public static IReadOnlyList<string> KindNames { get; } = new[]
    {
        "Text", "Long text", "Number", "Date", "Password", "Person", "Website", "Account",
    };

    private static readonly IReadOnlyList<ServiceFieldKind> KindValues = new[]
    {
        ServiceFieldKind.Text, ServiceFieldKind.LongText, ServiceFieldKind.Number,
        ServiceFieldKind.Date, ServiceFieldKind.Secret, ServiceFieldKind.Person,
        ServiceFieldKind.Website, ServiceFieldKind.Account,
    };

    private static readonly Random Keys = new();

    private ServiceDefinition? _existing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private string _typeName = string.Empty;

    public ObservableCollection<DesignerFieldDraft> Fields { get; } = new();

    public ObservableCollection<DesignerTableDraft> Tables { get; } = new();

    public string Title => $"SET UP {TypeName.ToUpperInvariant()}";

    public string Subtitle =>
        "Decide what every " + TypeName.ToLowerInvariant() + " record holds. "
        + "You can change this at any time.";

    /// <summary>Whether the structure on screen is worth saving.</summary>
    /// <remarks>
    /// At least one field, every label filled in and unique within its scope. The
    /// names this refuses are the names that would make a form unreadable: two
    /// fields called the same thing, a field called nothing.
    /// </remarks>
    public bool CanSave
    {
        get
        {
            if (Fields.Count == 0 || !LabelsAreUnique(Fields))
            {
                return false;
            }

            foreach (DesignerTableDraft table in Tables)
            {
                if (string.IsNullOrWhiteSpace(table.Label)
                    || table.Fields.Count == 0
                    || !LabelsAreUnique(table.Fields))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Prepares the designer for a type that has never been designed.</summary>
    public void ShowSetup(string typeName)
    {
        _existing = null;
        TypeName = typeName;

        Fields.ClearSafely();
        Tables.ClearSafely();

        // One row to start from rather than a blank panel: the empty designer is a
        // question, and a first row labelled with the type's own name is its answer.
        Fields.Add(Track(new DesignerFieldDraft(NewKey(), typeName, KindNames[0])));
    }

    /// <summary>Prepares the designer with a type's existing structure.</summary>
    public void ShowEdit(string typeName, ServiceDefinition definition)
    {
        _existing = definition;
        TypeName = typeName;

        Fields.ClearSafely();
        Tables.ClearSafely();

        foreach (ServiceFieldDef field in definition.Fields)
        {
            Fields.Add(Track(new DesignerFieldDraft(field.Key, field.Label, NameOf(field.Kind))));
        }

        foreach (ServiceTableDef table in definition.Tables)
        {
            DesignerTableDraft draft = new(table.Key, table.Label);

            foreach (ServiceFieldDef field in table.Fields)
            {
                draft.Fields.Add(Track(new DesignerFieldDraft(field.Key, field.Label, NameOf(field.Kind))));
            }

            Tables.Add(Track(draft));
        }
    }

    public void AddField()
    {
        Fields.Add(Track(new DesignerFieldDraft(NewKey(), string.Empty, KindNames[0])));
        OnPropertyChanged(nameof(CanSave));
    }

    public void RemoveField(DesignerFieldDraft field)
    {
        Fields.Remove(field);
        OnPropertyChanged(nameof(CanSave));
    }

    public void MoveField(DesignerFieldDraft field, int places)
    {
        int from = Fields.IndexOf(field);
        int to = Math.Clamp(from + places, 0, Fields.Count - 1);

        if (from != to)
        {
            Fields.Move(from, to);
        }
    }

    public void AddTable()
    {
        Tables.Add(Track(new DesignerTableDraft(NewKey(), string.Empty)));
        OnPropertyChanged(nameof(CanSave));
    }

    public void RemoveTable(DesignerTableDraft table)
    {
        Tables.Remove(table);
        OnPropertyChanged(nameof(CanSave));
    }

    /// <summary>Called by the view whenever a draft changed, to re-evaluate CanSave.</summary>
    public void Touch() => OnPropertyChanged(nameof(CanSave));

    /// <summary>
    /// Wires a field row into the save state: every keystroke in a label and every
    /// flip of a kind is a structure change, and the save button should know.
    /// </summary>
    private DesignerFieldDraft Track(DesignerFieldDraft draft)
    {
        draft.Changed += (_, _) => OnPropertyChanged(nameof(CanSave));
        return draft;
    }

    /// <summary>Wires a table block: its name and its rows both count.</summary>
    private DesignerTableDraft Track(DesignerTableDraft table)
    {
        table.Changed += (_, _) => OnPropertyChanged(nameof(CanSave));
        table.Fields.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CanSave));

        foreach (DesignerFieldDraft field in table.Fields)
        {
            Track(field);
        }

        return table;
    }

    public void AddTableField(DesignerTableDraft table)
    {
        table.Fields.Add(Track(new DesignerFieldDraft(NewKey(), string.Empty, KindNames[0])));
        OnPropertyChanged(nameof(CanSave));
    }

    public void RemoveTableField(DesignerTableDraft table, DesignerFieldDraft field)
    {
        table.Fields.Remove(field);
        OnPropertyChanged(nameof(CanSave));
    }

    public void MoveTableField(DesignerTableDraft table, DesignerFieldDraft field, int places)
    {
        int from = table.Fields.IndexOf(field);
        int to = Math.Clamp(from + places, 0, table.Fields.Count - 1);

        if (from != to)
        {
            table.Fields.Move(from, to);
        }
    }

    /// <summary>The structure on screen, ready for the store.</summary>
    public ServiceDefinition ToDefinition()
    {
        List<ServiceFieldDef> fields = Fields
            .Select(f => new ServiceFieldDef(f.Key, f.Label.Trim(), KindOf(f.KindName)))
            .ToList();

        List<ServiceTableDef> tables = Tables
            .Select(t => new ServiceTableDef(
                t.Key,
                t.Label.Trim(),
                t.Fields
                    .Select(f => new ServiceFieldDef(f.Key, f.Label.Trim(), KindOf(f.KindName)))
                    .ToList()))
            .ToList();

        return new ServiceDefinition
        {
            // The id is the service type's id; the caller sets it, because only the
            // caller knows which type this designer was opened for.
            FieldsJson = ServiceDefinition.PackFields(fields),
            TablesJson = ServiceDefinition.PackTables(tables),
            ConfiguredAt = _existing?.ConfiguredAt,
        };
    }

    private static bool LabelsAreUnique(IEnumerable<DesignerFieldDraft> fields)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (DesignerFieldDraft field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Label))
            {
                return false;
            }

            if (!seen.Add(field.Label.Trim()))
            {
                return false;
            }
        }

        return true;
    }

    private static string NewKey() => "f" + Keys.Next(0x100000, 0xFFFFFF).ToString("x6");

    private static string NameOf(ServiceFieldKind kind)
        => KindNames[(int)kind];

    private static ServiceFieldKind KindOf(string name)
    {
        // The names are the picker's vocabulary, so the picker never hands over
        // anything else - but the picker order and the enum order are two lists
        // that happen to line up, and a loop makes the pairing explicit.
        for (int i = 0; i < KindNames.Count; i++)
        {
            if (string.Equals(KindNames[i], name, StringComparison.Ordinal))
            {
                return KindValues[i];
            }
        }

        return KindValues[0];
    }
}
