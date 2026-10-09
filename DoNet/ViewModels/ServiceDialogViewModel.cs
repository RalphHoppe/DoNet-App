using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Controls;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>Which of the three jobs the one service dialog is doing.</summary>
public enum ServiceDialogMode
{
    Preview,
    Add,
    Edit,
}

/// <summary>
/// Backs the service dialog in all three of its modes.
/// </summary>
/// <remarks>
/// One dialog rather than three, for the same reason the other three are one: the
/// modes differ in which controls are editable and what the button says, not in
/// what they contain. A service type is the smallest record in the app - a name,
/// a description and a note - so this is also the plainest expression of the
/// pattern the other dialogs elaborate on.
///</remarks>
public sealed partial class ServiceDialogViewModel : ObservableObject
{
    private Service? _original;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(IsPreview))]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(FieldKind))]
    private ServiceDialogMode _mode = ServiceDialogMode.Preview;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _note = string.Empty;

    /// <summary>Read-only display of the store-assigned record number.</summary>
    [ObservableProperty] private string _idDisplay = string.Empty;

    public string Title => Mode switch
    {
        ServiceDialogMode.Add => "ADD SERVICE",
        ServiceDialogMode.Edit => "EDIT SERVICE",
        _ => "SERVICE INFO",
    };

    public string Subtitle => Mode switch
    {
        ServiceDialogMode.Add => "Create a service type.",
        ServiceDialogMode.Edit => "Edit this service type \u00B7 # cannot be changed.",
        _ => "Full service record",
    };

    public string PrimaryActionText => Mode switch
    {
        ServiceDialogMode.Add => "ADD SERVICE",
        ServiceDialogMode.Edit => "SAVE CHANGES",
        _ => "EDIT RECORD",
    };

    public bool IsPreview => Mode == ServiceDialogMode.Preview;

    public bool IsEditing => Mode != ServiceDialogMode.Preview;

    /// <summary>Display or editable, for every FieldCell on the dialog at once.</summary>
    public FieldCellKind FieldKind =>
        IsPreview ? FieldCellKind.Display : FieldCellKind.Editable;

    public void ShowPreview(Service service)
    {
        _original = service;
        Fill(service);
        Mode = ServiceDialogMode.Preview;
    }

    public void ShowAdd()
    {
        _original = null;
        Fill(new Service());
        IdDisplay = string.Empty;
        Mode = ServiceDialogMode.Add;
    }

    public void ShowEdit(Service service)
    {
        _original = service;
        Fill(service);
        Mode = ServiceDialogMode.Edit;
    }

    /// <summary>Preview turns into edit in place.</summary>
    public void SwitchToEdit() => Mode = ServiceDialogMode.Edit;

    /// <summary>
    /// True when the form has enough to save.
    /// </summary>
    /// <remarks>
    /// Only the name is required. A description and a note are worth having and not
    /// worth insisting on - the six seeded types ship without either.
    /// </remarks>
    public bool CanSave => !string.IsNullOrWhiteSpace(Name);

    /// <summary>
    /// The form as a record, ready for the store.
    /// </summary>
    /// <remarks>
    /// Built from a clone of the original so the id survives an edit untouched.
    /// </remarks>
    public Service ToService()
    {
        Service service = _original?.Clone() ?? new Service();

        service.Name = Name.Trim();
        service.Description = Description.Trim();
        service.Note = Note.Trim();

        return service;
    }

    private void Fill(Service service)
    {
        Name = service.Name;
        Description = service.Description;
        Note = service.Note;

        IdDisplay = service.Id > 0 ? service.IdDisplay : string.Empty;
    }
}
