using System;

namespace DoNet.Models;

/// <summary>
/// A kind of service a site can offer: VPS, Domain, SMS, and so on.
/// </summary>
/// <remarks>
/// The catalog behind the Services tab. It is deliberately not the record of any
/// service anyone owns - it is the *vocabulary*: the types the records to come
/// (a VPS, a phone number) will be filed under, and the things a site can be
/// marked as offering.
///
/// Mutable class rather than a record, for the same reason <see cref="Website"/> is:
/// the edit form works on a copy and commits it, and value equality would make two
/// blank services the same service.
///</remarks>
public sealed class Service
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    /// <summary>Heading on the card. Falls back when the type has no name yet.</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? "Service record" : Name.Trim();

    /// <summary>The record number as the card's caption reads it.</summary>
    public string IdDisplay => Id > 0 ? $"#{Id}" : string.Empty;

    public Service Clone() => (Service)MemberwiseClone();
}
