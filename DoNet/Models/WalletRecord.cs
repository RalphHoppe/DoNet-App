using System.ComponentModel.DataAnnotations.Schema;

namespace DoNet.Models;

/// <summary>
/// One wallet record - the fourth hand-built service type's data, column for
/// column with the user's Wallets sheet.
/// </summary>
/// <remarks>
/// <para>
/// A wallet hangs off a person the way a VPS does: an optional nullable key
/// that clears when the person is deleted, because the wallet exists whether
/// or not the directory has met its owner yet.
/// </para>
/// <para>
/// The money is a trio with one relation in it: <see cref="ExchangeRate"/> is
/// what one unit of <see cref="Currency"/> is worth in dollars, so
/// <see cref="BalanceDollars"/> is the balance times the rate. The form fills
/// whichever of the two the user leaves for it to do, the same interlock the
/// VPS form runs between its Bitcoin and rate fields.
/// </para>
/// <para>
/// <see cref="Password"/> and <see cref="RecoveryWords"/> are the record's
/// secrets - the recovery words especially: a seed phrase spends the wallet
/// all by itself, so it is treated exactly like a password, masked until
/// deliberately revealed and never shown on a card.
/// </para>
///</remarks>
public sealed class WalletRecord
{
    public int Id { get; set; }

    /// <summary>The person this wallet belongs to, if any; cleared when the person is deleted.</summary>
    public int? PersonId { get; set; }

    /// <summary>The wallet's brand, e.g. "MetaMask". The one required field.</summary>
    public string Wallet { get; set; } = string.Empty;

    /// <summary>The wallet's number or address, as the provider shows it.</summary>
    public string WalletNumber { get; set; } = string.Empty;

    /// <summary>The currency the balance is held in, as a three-letter code.</summary>
    public string Currency { get; set; } = "BTC";

    /// <summary>
    /// The balance in <see cref="Currency"/>, when entered. Decimal is stored
    /// as text by SQLite; the app only ever computes over it in C#.
    /// </summary>
    public decimal? Balance { get; set; }

    /// <summary>The balance in dollars, when entered; filled from the rate in the form.</summary>
    public decimal? BalanceDollars { get; set; }

    /// <summary>What one unit of the currency is worth in dollars, when entered.</summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>The wallet's password, if it has one. Never trimmed: spaces can be part of it.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// The recovery words - the seed phrase. Kept as typed and masked like a
    /// password; never shown on a card.
    /// </summary>
    public string RecoveryWords { get; set; } = string.Empty;

    /// <summary>Anything worth remembering about this wallet.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// The owner's display name, filled on reads for the card's caption. Not a
    /// column: the name lives on the person, and this is the directory's
    /// one-map fill, the same job <c>NodeName</c> does for the VPS records.
    /// </summary>
    [NotMapped]
    public string? PersonName { get; set; }

    /// <summary>The card's caption: whose wallet this is, or its own number.</summary>
    [NotMapped]
    public string Caption =>
        string.IsNullOrWhiteSpace(PersonName)
            ? (WalletNumber.Length > 0 ? $"#{Id} · {WalletNumber}" : $"Wallet · #{Id}")
            : PersonName;
}
