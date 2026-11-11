using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoNet.Models;

/// <summary>
/// A person record. All eighteen columns from the agreed schema.
/// </summary>
/// <remarks>
/// Deliberately a plain mutable class rather than a record: the edit form writes to a
/// copy and commits it, so value equality would be actively unhelpful - two different
/// people with every field blank are not the same person.
///
/// <see cref="Id"/> and <see cref="CreatedAt"/> are assigned by the store on insert,
/// which is what the add form means by "Assigned on save".
/// </remarks>
public sealed class Person
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string EmailPassword { get; set; } = string.Empty;

    /// <summary>
    /// Recovery for the <see cref="Email"/> account above - not a second contact for
    /// the person. The dialogs say so explicitly, because the distinction is not
    /// obvious from the field names alone.
    /// </summary>
    public string RecoveryEmail { get; set; } = string.Empty;

    public string RecoveryPassword { get; set; } = string.Empty;
    public string RecoveryWords { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string Note { get; set; } = string.Empty;

    /// <summary>Heading on the card. Falls back when the person has no name yet.</summary>
    [NotMapped]
    public string DisplayName =>
        string.IsNullOrWhiteSpace($"{FirstName} {LastName}".Trim())
            ? "Person record"
            : $"{FirstName} {LastName}".Trim();

    /// <summary>Creation stamp, formatted for display. Empty until the store assigns one.</summary>
    [NotMapped]
    public string CreatedAtDisplay =>
        CreatedAt == default ? string.Empty : CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public Person Clone() => (Person)MemberwiseClone();
}
