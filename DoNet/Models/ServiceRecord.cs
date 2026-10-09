using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace DoNet.Models;

/// <summary>
/// One record of a user-defined service type - one VPS, one phone number - or one
/// row of a table attached to such a record.
/// </summary>
/// <remarks>
/// The same table serves both roles. A top-level record has a null
/// <see cref="ParentRecordId"/>; a table row carries the id of the record it belongs
/// to and the <see cref="TableKey"/> of the table it fills. One table for both is
/// deliberate: rows are records with a parent, and giving them their own table would
/// duplicate every column and every query for the sake of a null check.
///
/// The values live in <see cref="DataJson"/> as one JSON object keyed by the field
/// keys of the type's <see cref="ServiceDefinition"/>. Relationships store the
/// related record's id as a string; the label it stands for is resolved when the
/// value is shown, from the option lists the directories already know how to load.
///</remarks>
public sealed class ServiceRecord
{
    public int Id { get; set; }

    /// <summary>The service type this record belongs to.</summary>
    public int ServiceTypeId { get; set; }

    /// <summary>The record this row belongs to, or null for the record itself.</summary>
    public int? ParentRecordId { get; set; }

    /// <summary>Which of the type's tables this row fills, or empty for the record.</summary>
    public string TableKey { get; set; } = string.Empty;

    /// <summary>
    /// The record's name - the value of the type's first text field, or a fallback
    /// until one is saved. Kept as its own column because every list, search and
    /// card starts from it.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The field values by key. <see cref="Data"/> is the one to use.</summary>
    public string DataJson { get; set; } = "{}";

    /// <summary>
    /// Every visible value in one line, kept for the search. Secrets are excluded -
    /// a typed fragment of one password must not be able to confirm another.
    /// </summary>
    public string SearchText { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>The field values by field key.</summary>
    [NotMapped]
    public IReadOnlyDictionary<string, string> Data
    {
        get
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(DataJson)
                    ?? new Dictionary<string, string>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, string>();
            }
        }
    }

    /// <summary>One field's value, or empty when the record never had one.</summary>
    [NotMapped]
    public string Value(string key) => Data.TryGetValue(key, out string? value) ? value : string.Empty;

    /// <summary>The record number, as the card's caption reads it.</summary>
    [NotMapped]
    public string IdDisplay => Id > 0 ? $"#{Id}" : string.Empty;

    /// <summary>Creation stamp, formatted for display. Empty until the store assigns one.</summary>
    [NotMapped]
    public string CreatedAtDisplay =>
        CreatedAt == default ? string.Empty : CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>Serializes field values for storage.</summary>
    public static string PackData(IReadOnlyDictionary<string, string> data)
        => JsonSerializer.Serialize(data);

    public ServiceRecord Clone() => (ServiceRecord)MemberwiseClone();
}
