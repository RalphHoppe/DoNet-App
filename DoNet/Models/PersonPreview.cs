namespace DoNet.Models;

/// <summary>
/// The subset of a person shown on a directory card.
/// </summary>
/// <remarks>
/// A person has eighteen columns; a card shows nine of them. The remaining nine
/// (State, City, Street, Postal Code, Phone Number, Email Password, Recovery,
/// Recovery Password, Recovery Words) belong to the full record form, which has not
/// been designed yet. Deliberately not modelled here: inventing a shape for a screen
/// that does not exist would guess at decisions that are not mine to make.
///
/// Every value is a string. These are previews for display, not a persistence model -
/// the real entity will type Date of Birth and Created At properly when the data layer
/// is built. Keeping them strings here means the card renders whatever the source
/// provides, including the em-dash placeholder the design calls for.
/// </remarks>
public sealed record PersonPreview(
    string Id,
    string FirstName,
    string LastName,
    string Gender,
    string DateOfBirth,
    string Country,
    string Email,
    string CreatedAt,
    string Note)
{
    /// <summary>Shown as the card heading.</summary>
    public string DisplayName { get; init; } = "Person record";

    /// <summary>Shown beneath the heading.</summary>
    public string Caption { get; init; } = "Record preview";

    /// <summary>
    /// The placeholder every empty field renders. An em-dash, not a hyphen - the design
    /// uses the long dash and at 14px the difference is visible.
    /// </summary>
    public const string Blank = "\u2014";

    /// <summary>A card with every field blank, matching the supplied design exactly.</summary>
    public static PersonPreview Placeholder() => new(
        Blank, Blank, Blank, Blank, Blank, Blank, Blank, Blank, Blank);

    /// <summary>
    /// Fields the search box matches against. Only what a card actually shows, so a
    /// hit always corresponds to something the user can see.
    /// </summary>
    public bool Matches(string term) =>
        string.IsNullOrWhiteSpace(term)
        || DisplayName.Contains(term, System.StringComparison.OrdinalIgnoreCase)
        || FirstName.Contains(term, System.StringComparison.OrdinalIgnoreCase)
        || LastName.Contains(term, System.StringComparison.OrdinalIgnoreCase)
        || Country.Contains(term, System.StringComparison.OrdinalIgnoreCase)
        || Email.Contains(term, System.StringComparison.OrdinalIgnoreCase);
}
