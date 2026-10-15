using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoNet.Models;

/// <summary>
/// A login held on one of the saved websites.
/// </summary>
/// <remarks>
/// The first record in the app with a real foreign key. <see cref="Person"/> and
/// <see cref="Website"/> stand alone; an account cannot exist without the site it is
/// an account on, which is why <see cref="WebsiteId"/> is required rather than
/// nullable and why the add form offers a list of saved websites instead of a free
/// text box.
///
/// Mutable class rather than a record, for the same reason the other two are: the
/// edit form works on a copy and commits it, and value equality would make two blank
/// accounts the same account.
/// </remarks>
public sealed class Account
{
    public int Id { get; set; }

    /// <summary>The website this login belongs to.</summary>
    public int WebsiteId { get; set; }

    /// <summary>
    /// The site itself, loaded alongside the account so the card can name it.
    /// </summary>
    /// <remarks>
    /// Nullable because the property is only populated on reads that ask for it. A
    /// stored account always has a website behind it; the database enforces that.
    /// </remarks>
    public Website? Website { get; set; }

    /// <summary>Optional, per the schema: some sites identify an account only by email.</summary>
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// Not shown anywhere yet.
    /// </summary>
    /// <remarks>
    /// Recorded because it cannot be recovered later: an account saved today without
    /// a stamp can never be told apart from one saved last year. The design has no
    /// place for it, so nothing displays it - this is the cheap half of the decision,
    /// taken now so the expensive half stays available.
    /// </remarks>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Heading on the card. Falls back when the account has no username yet.</summary>
    [NotMapped]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Username) ? "Account record" : Username.Trim();

    /// <summary>
    /// How the website reads on a card or in the preview. The same label the picker
    /// shows, so the value a user chose is the value they see afterwards.
    /// </summary>
    [NotMapped]
    public string WebsiteDisplay
    {
        get
        {
            return Website is null ? string.Empty : Website.PickerLabel;
        }
    }

    /// <summary>Creation stamp, formatted for display. Empty until the store assigns one.</summary>
    [NotMapped]
    public string CreatedAtDisplay =>
        CreatedAt == default ? string.Empty : CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public Account Clone() => (Account)MemberwiseClone();
}
