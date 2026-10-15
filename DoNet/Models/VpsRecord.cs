using System.ComponentModel.DataAnnotations.Schema;

namespace DoNet.Models;

/// <summary>
/// One VPS record - the first hand-built service type's data, column for column
/// with the user's VPS sheet.
/// </summary>
/// <remarks>
/// <para>
/// The three directories a VPS can point at - person, site, account - are
/// optional nullable keys: a VPS often exists before any of those do. The
/// system row is not optional in the design, but the store tolerates its
/// absence so a half-finished form is never forced to invent one.
/// </para>
/// <para>
/// <see cref="CreatedAt"/> and <see cref="OutDate"/> are the user's entries, by
/// explicit instruction - unlike the rest of the app, nothing stamps them
/// automatically.
/// </para>
/// <para>
/// <see cref="NodeVpsId"/> is the self-reference: null means this VPS is its own
/// main. The user's sheet stores the record's own id to say that; the store
/// keeps the cleaner "no parent" reading and <see cref="NodeName"/> renders
/// either form. Children of a deleted main are reparented to null - they become
/// their own main - because a dangling node id would be a broken record, and a
/// delete that refuses is not a delete the design has room for.
/// </para>
///</remarks>
public sealed class VpsRecord
{
    public int Id { get; set; }

    /// <summary>The person this VPS belongs to, if any; cleared when the person is deleted.</summary>
    public int? PersonId { get; set; }

    /// <summary>The provider's site, if any; cleared when the site is deleted.</summary>
    public int? WebsiteId { get; set; }

    /// <summary>The account on that site, if any; cleared when the account is deleted.</summary>
    public int? AccountId { get; set; }

    /// <summary>
    /// The wallet this VPS was paid with, if any; cleared when the wallet is
    /// deleted. The payment already happened, so the pointer is history, not
    /// membership - the VPS stays when the wallet goes.
    /// </summary>
    public int? WalletId { get; set; }

    /// <summary>The record's name, e.g. "A" or "A-N". The one required field.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The 1:1 system row, loaded on reads that ask for it.</summary>
    public VpsSystemInfo? System { get; set; }

    /// <summary>
    /// The balance in <see cref="Currency"/>, when entered. Decimal is stored as
    /// text by SQLite; the app only ever computes over it in C#, never in SQL.
    /// </summary>
    public decimal? Balance { get; set; }

    /// <summary>The currency the balance is held in, as a three-letter code.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// The balance in Bitcoin, when entered. Filled automatically from the
    /// exchange rate in the form, and either can be typed to fill the other.
    /// </summary>
    public decimal? BalanceBtc { get; set; }

    /// <summary>How much one Bitcoin is worth in <see cref="Currency"/>.</summary>
    public decimal? ExchangeRate { get; set; }

    /// <summary>The user's entry, not the store's stamp - the sheet's "Created At".</summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>When the subscription runs out, as the user enters it.</summary>
    public string OutDate { get; set; } = string.Empty;

    /// <summary>The cycle's count, e.g. 3 of "3m"; null when no cycle was given.</summary>
    public int? CycleValue { get; set; }

    /// <summary>The cycle's unit: d, w, m or y. Empty when no cycle was given.</summary>
    public string CycleUnit { get; set; } = string.Empty;

    /// <summary>
    /// The VPS this one is connected to. Null means this VPS is the main one -
    /// the sheet's "same id as itself" - and a delete reparents children here.
    /// </summary>
    public int? NodeVpsId { get; set; }

    public string Country { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    /// <summary>
    /// The parent VPS's name, or empty when this record is the main one. Filled
    /// by the directory on read; not stored.
    /// </summary>
    [NotMapped]
    public string NodeName { get; set; } = string.Empty;

    /// <summary>True when this record is its own main - no parent.</summary>
    [NotMapped]
    public bool IsMain => NodeVpsId is null;

    /// <summary>The cycle as the sheet writes it, e.g. "3m"; empty when unset.</summary>
    [NotMapped]
    public string CycleLabel =>
        CycleValue is { } value && CycleUnit.Length == 1 ? $"{value}{CycleUnit}" : string.Empty;
}
