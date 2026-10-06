using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DoNet.Controls;
using DoNet.Models;

namespace DoNet.ViewModels;

/// <summary>Which of the three faces the person dialog is wearing.</summary>
public enum PersonDialogMode
{
    /// <summary>Read-only, with COPY on every field. "PREVIEW ALL INFO".</summary>
    Preview,

    /// <summary>A blank form. "ADD PERSON".</summary>
    Add,

    /// <summary>The same form, pre-filled. Reached from the preview's EDIT RECORD.</summary>
    Edit,
}

/// <summary>
/// Backs the person dialog in all three modes.
/// </summary>
/// <remarks>
/// One view model rather than three, because the two supplied designs are the same grid
/// of eighteen fields differing only in whether the cells are readable or writable.
/// Splitting them would mean maintaining that grid twice.
///
/// The fields are held as loose strings rather than as a <see cref="Person"/> so that
/// cancelling costs nothing: the record is only assembled in <see cref="ToPerson"/>,
/// which runs on save. Nothing can half-edit the stored object.
/// </remarks>
public sealed partial class PersonDialogViewModel : ObservableObject
{
    /// <summary>The record being viewed or edited. Null when adding.</summary>
    private Person? _original;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    [NotifyPropertyChangedFor(nameof(PrimaryActionText))]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(IsPreview))]
    [NotifyPropertyChangedFor(nameof(FieldKind))]
    [NotifyPropertyChangedFor(nameof(AutoFieldKind))]
    private PersonDialogMode _mode = PersonDialogMode.Preview;

    [ObservableProperty] private string _firstName = string.Empty;
    [ObservableProperty] private string _lastName = string.Empty;
    [ObservableProperty] private string _gender = string.Empty;
    [ObservableProperty] private string _dateOfBirth = string.Empty;
    [ObservableProperty] private string _country = string.Empty;
    [ObservableProperty] private string _state = string.Empty;
    [ObservableProperty] private string _city = string.Empty;
    [ObservableProperty] private string _street = string.Empty;
    [ObservableProperty] private string _postalCode = string.Empty;
    [ObservableProperty] private string _phoneNumber = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _emailPassword = string.Empty;
    [ObservableProperty] private string _recoveryEmail = string.Empty;
    [ObservableProperty] private string _recoveryPassword = string.Empty;
    [ObservableProperty] private string _recoveryWords = string.Empty;
    [ObservableProperty] private string _note = string.Empty;

    /// <summary>Shown in the "#" cell. Empty while adding, when it does not exist yet.</summary>
    [ObservableProperty] private string _idDisplay = string.Empty;

    [ObservableProperty] private string _createdAtDisplay = string.Empty;

    public bool IsEditing => Mode is PersonDialogMode.Add or PersonDialogMode.Edit;

    public bool IsPreview => Mode == PersonDialogMode.Preview;

    /// <summary>How an ordinary cell presents itself in the current mode.</summary>
    public FieldCellKind FieldKind =>
        IsEditing ? FieldCellKind.Editable : FieldCellKind.Display;

    /// <summary>
    /// How "#" and "Created At" present themselves. Greyed and inert while adding,
    /// because the store has not assigned them yet; ordinary read-only text afterwards,
    /// including while editing - they exist by then, they just cannot be changed.
    /// </summary>
    public FieldCellKind AutoFieldKind =>
        Mode == PersonDialogMode.Add ? FieldCellKind.Automatic : FieldCellKind.Display;

    public string Title => Mode switch
    {
        PersonDialogMode.Add => "ADD PERSON",
        PersonDialogMode.Edit => "EDIT PERSON",
        _ => "PREVIEW ALL INFO",
    };

    public string Subtitle => Mode switch
    {
        PersonDialogMode.Add =>
            "Create a person record \u00b7 # and Created At are assigned automatically on save.",
        PersonDialogMode.Edit =>
            "Edit this person record \u00b7 # and Created At cannot be changed.",
        _ => "Full person record",
    };

    public string PrimaryActionText => Mode switch
    {
        PersonDialogMode.Add => "ADD PERSON",
        PersonDialogMode.Edit => "SAVE CHANGES",
        _ => "EDIT RECORD",
    };

    /// <summary>
    /// Why the recovery fields are grouped and labelled the way they are. Verbatim from
    /// the design - the distinction it draws is not obvious from the field names.
    /// </summary>
    public string RecoveryNote =>
        "Recovery Email, Password and Words recover the primary Email account above "
        + "\u2014 not additional person contacts.";

    /// <summary>Opens on an existing record, read-only.</summary>
    public void ShowPreview(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);

        _original = person;
        Fill(person);
        Mode = PersonDialogMode.Preview;
    }

    /// <summary>Opens a blank form.</summary>
    public void ShowAdd()
    {
        _original = null;
        Fill(new Person());
        IdDisplay = string.Empty;
        CreatedAtDisplay = string.Empty;
        Mode = PersonDialogMode.Add;
    }

    /// <summary>Opens an existing record for editing.</summary>
    public void ShowEdit(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);

        _original = person;
        Fill(person);
        Mode = PersonDialogMode.Edit;
    }

    /// <summary>
    /// EDIT RECORD, from the preview. Switches this dialog in place rather than closing
    /// and opening another one.
    /// </summary>
    public void SwitchToEdit()
    {
        if (_original is { } person)
        {
            ShowEdit(person);
        }
    }

    /// <summary>
    /// Assembles the record to save. Id and CreatedAt come from the original, never
    /// from the form - the store owns both.
    /// </summary>
    public Person ToPerson()
    {
        Person person = _original?.Clone() ?? new Person();

        person.FirstName = FirstName.Trim();
        person.LastName = LastName.Trim();
        person.Gender = Gender.Trim();
        person.DateOfBirth = DateOfBirth.Trim();
        person.Country = Country.Trim();
        person.State = State.Trim();
        person.City = City.Trim();
        person.Street = Street.Trim();
        person.PostalCode = PostalCode.Trim();
        person.PhoneNumber = PhoneNumber.Trim();
        person.Email = Email.Trim();

        // Secrets are not trimmed: leading or trailing whitespace can be part of a
        // password, and silently removing it would store something that does not work.
        person.EmailPassword = EmailPassword;
        person.RecoveryEmail = RecoveryEmail.Trim();
        person.RecoveryPassword = RecoveryPassword;
        person.RecoveryWords = RecoveryWords;

        person.Note = Note.Trim();

        return person;
    }

    private void Fill(Person person)
    {
        FirstName = person.FirstName;
        LastName = person.LastName;
        Gender = person.Gender;
        DateOfBirth = person.DateOfBirth;
        Country = person.Country;
        State = person.State;
        City = person.City;
        Street = person.Street;
        PostalCode = person.PostalCode;
        PhoneNumber = person.PhoneNumber;
        Email = person.Email;
        EmailPassword = person.EmailPassword;
        RecoveryEmail = person.RecoveryEmail;
        RecoveryPassword = person.RecoveryPassword;
        RecoveryWords = person.RecoveryWords;
        Note = person.Note;

        IdDisplay = person.Id > 0 ? person.Id.ToString() : string.Empty;
        CreatedAtDisplay = person.CreatedAtDisplay;
    }
}
