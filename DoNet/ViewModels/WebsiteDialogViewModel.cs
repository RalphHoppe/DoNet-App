using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Controls;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>Which of the three jobs the one website dialog is doing.</summary>
public enum WebsiteDialogMode
{
    Preview,
    Add,
    Edit,
}

/// <summary>
/// Backs the website dialog in all three of its modes.
/// </summary>
/// <remarks>
/// One dialog rather than three, for the same reason the person dialog is one: the
/// three differ in which controls are editable and what the button says, not in what
/// they contain, and three near-identical layouts drift apart.
/// </remarks>
public sealed partial class WebsiteDialogViewModel : ObservableObject
{
    private Website? _original;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    [NotifyPropertyChangedFor(nameof(TitleIcon))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(IsPreview))]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(FieldKind))]
    private WebsiteDialogMode _mode = WebsiteDialogMode.Preview;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _domain = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _note = string.Empty;

    [ObservableProperty] private string _idDisplay = string.Empty;
    [ObservableProperty] private string _createdAtDisplay = string.Empty;

    /// <summary>Text in the "add another method" box.</summary>
    [ObservableProperty] private string _newMethod = string.Empty;

    /// <summary>Every method the form offers, built-ins first.</summary>
    public ObservableCollection<PaymentMethodChoice> Choices { get; } = new();

    /// <summary>The methods currently selected, in catalog order. Drives the chips.</summary>
    public ObservableCollection<string> Selected { get; } = new();

    public string Title => Mode switch
    {
        WebsiteDialogMode.Add => "ADD WEBSITE",
        WebsiteDialogMode.Edit => "EDIT WEBSITE",
        _ => "PREVIEW ALL INFO",
    };

    /// <summary>The mark beside the title, which tracks the mode.</summary>
    public string TitleIcon => Mode switch
    {
        WebsiteDialogMode.Add => "Plus",
        WebsiteDialogMode.Edit => "Pencil",
        _ => "Info",
    };

    public string Subtitle => Mode switch
    {
        WebsiteDialogMode.Add => "Create a website record.",
        WebsiteDialogMode.Edit => "Edit this website record · # and Created At cannot be changed.",
        _ => "Full website record",
    };

    public string PrimaryActionText => Mode switch
    {
        WebsiteDialogMode.Add => "ADD WEBSITE",
        WebsiteDialogMode.Edit => "SAVE CHANGES",
        _ => "EDIT RECORD",
    };

    public bool IsPreview => Mode == WebsiteDialogMode.Preview;

    public bool IsEditing => Mode != WebsiteDialogMode.Preview;

    /// <summary>Display or editable, for every FieldCell on the dialog at once.</summary>
    public FieldCellKind FieldKind =>
        IsPreview ? FieldCellKind.Display : FieldCellKind.Editable;

    public void ShowPreview(Website website, IReadOnlyList<string> options)
    {
        _original = website;
        Fill(website, options);
        Mode = WebsiteDialogMode.Preview;
    }

    public void ShowAdd(IReadOnlyList<string> options)
    {
        _original = null;
        Fill(new Website(), options);
        IdDisplay = string.Empty;
        CreatedAtDisplay = string.Empty;
        Mode = WebsiteDialogMode.Add;
    }

    public void ShowEdit(Website website, IReadOnlyList<string> options)
    {
        _original = website;
        Fill(website, options);
        Mode = WebsiteDialogMode.Edit;
    }

    /// <summary>Turns a preview into an edit without rebuilding anything.</summary>
    public void SwitchToEdit() => Mode = WebsiteDialogMode.Edit;

    /// <summary>
    /// Adds whatever is in the "another method" box, selects it, and clears the box.
    /// </summary>
    /// <returns>
    /// The method that was added, or null if the box was blank or already offered -
    /// the caller uses this to decide whether it is worth saving to the catalog.
    /// </returns>
    public string? CommitNewMethod()
    {
        string candidate = NewMethod.Trim();

        if (candidate.Length == 0)
        {
            return null;
        }

        NewMethod = string.Empty;

        PaymentMethodChoice? existing =
            Choices.FirstOrDefault(c => PaymentMethodCatalog.Matches(c.Name, candidate));

        if (existing is not null)
        {
            // Already on the list - tick it rather than adding a duplicate under a
            // different casing.
            existing.IsSelected = true;
            Sync();
            return null;
        }

        PaymentMethodChoice added = new(candidate, isSelected: true);
        added.PropertyChanged += (_, _) => Sync();
        Choices.Add(added);
        Sync();

        return candidate;
    }

    /// <summary>Unticks a method, which removes its chip.</summary>
    public void Deselect(string method)
    {
        PaymentMethodChoice? choice =
            Choices.FirstOrDefault(c => PaymentMethodCatalog.Matches(c.Name, method));

        if (choice is not null)
        {
            choice.IsSelected = false;
        }
    }

    public Website ToWebsite()
    {
        Website website = _original?.Clone() ?? new Website();

        website.Name = Name.Trim();
        website.Domain = Domain.Trim();
        website.Description = Description.Trim();
        website.Note = Note.Trim();
        website.PaymentMethods = Selected.ToList();

        return website;
    }

    private void Fill(Website website, IReadOnlyList<string> options)
    {
        Name = website.Name;
        Domain = website.Domain;
        Description = website.Description;
        Note = website.Note;
        NewMethod = string.Empty;

        IdDisplay = website.Id > 0 ? website.Id.ToString() : string.Empty;
        CreatedAtDisplay = website.CreatedAtDisplay;

        IReadOnlyList<string> chosen = website.PaymentMethods;

        Choices.ClearSafely();

        // The record's own methods are merged in too: a method deleted from the
        // catalog must still show on a record that uses it, or editing would silently
        // drop it.
        foreach (string option in PaymentMethodCatalog.Merge(options.Concat(chosen)))
        {
            PaymentMethodChoice choice = new(
                option,
                chosen.Any(c => PaymentMethodCatalog.Matches(c, option)));

            choice.PropertyChanged += (_, _) => Sync();
            Choices.Add(choice);
        }

        Sync();
    }

    /// <summary>Rebuilds the chip list from the ticked boxes.</summary>
    private void Sync()
    {
        Selected.ClearSafely();

        foreach (PaymentMethodChoice choice in Choices.Where(c => c.IsSelected))
        {
            Selected.Add(choice.Name);
        }
    }
}
