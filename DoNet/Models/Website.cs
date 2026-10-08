using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace DoNet.Models;

/// <summary>
/// A website record: what the site is, and how you can pay on it.
/// </summary>
/// <remarks>
/// Seven columns, matching the design: the record number and creation stamp are
/// assigned by the store, the rest are the user's.
///
/// Mutable class rather than a record, for the same reason <see cref="Person"/> is:
/// the edit form works on a copy and commits it, and value equality would make two
/// blank websites the same website.
/// </remarks>
public sealed class Website
{
    /// <summary>
    /// Separator for <see cref="PaymentMethods"/> inside <see cref="PaymentMethodsRaw"/>.
    /// </summary>
    /// <remarks>
    /// ASCII Unit Separator. Deliberately not a comma: payment methods are free text,
    /// and "Visa, Mastercard" typed as one entry would silently split in two. A control
    /// character cannot be typed into a single-line text box and will not survive a
    /// paste as anything else, so it cannot collide with a real value.
    /// </remarks>
    public const char MethodSeparator = '\u001F';

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The selected payment methods, stored as one delimited column.
    /// </summary>
    /// <remarks>
    /// A join table would be the textbook shape, and it is the wrong trade here. The
    /// directory reads whole pages of records and never queries by method, so a
    /// relationship would add an Include to every read and a second round trip to
    /// every write to support a query nobody makes. One column also keeps the method
    /// list inside the same encrypted row as the record it belongs to.
    ///
    /// This is the storage form; <see cref="PaymentMethods"/> is the one to use.
    /// </remarks>
    public string PaymentMethodsRaw { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The selected payment methods, in the order they were chosen.</summary>
    [NotMapped]
    public IReadOnlyList<string> PaymentMethods
    {
        get => string.IsNullOrEmpty(PaymentMethodsRaw)
            ? Array.Empty<string>()
            : PaymentMethodsRaw.Split(MethodSeparator, StringSplitOptions.RemoveEmptyEntries);

        set => PaymentMethodsRaw = value is null
            ? string.Empty
            : string.Join(MethodSeparator, value.Where(m => !string.IsNullOrWhiteSpace(m)));
    }

    /// <summary>Heading on the card. Falls back when the site has no name yet.</summary>
    [NotMapped]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? "Website record" : Name.Trim();

    /// <summary>The methods as one readable line, for the card's preview cell.</summary>
    [NotMapped]
    public string PaymentMethodsDisplay => string.Join(", ", PaymentMethods);

    /// <summary>Creation stamp, formatted for display. Empty until the store assigns one.</summary>
    [NotMapped]
    public string CreatedAtDisplay =>
        CreatedAt == default ? string.Empty : CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public Website Clone() => (Website)MemberwiseClone();
}
