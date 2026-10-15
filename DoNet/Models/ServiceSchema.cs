using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoNet.Models;

/// <summary>What a user-defined service field can hold.</summary>
public enum ServiceFieldKind
{
    /// <summary>One line of text.</summary>
    Text,

    /// <summary>Wrapping text with newlines, like a note.</summary>
    LongText,

    /// <summary>A number, stored as typed.</summary>
    Number,

    /// <summary>A date, picked from the calendar.</summary>
    Date,

    /// <summary>A password: masked on cards, generated and revealed in the form.</summary>
    Secret,

    /// <summary>A link to a person record.</summary>
    Person,

    /// <summary>A link to a website record.</summary>
    Website,

    /// <summary>A link to an account record.</summary>
    Account,
}

/// <summary>
/// One field a service type's records carry, as the user defined it.
/// </summary>
/// <param name="Key">
/// The stable identity of the field inside <see cref="ServiceRecord.Data"/>. Labels
/// can be renamed freely; the key is what the stored values hang off, and it never
/// changes after the structure is saved.
/// </param>
/// <param name="Label">What the user calls the field - "VPS Name", "Balance".</param>
/// <param name="Kind">What the field holds; see <see cref="ServiceFieldKind"/>.</param>
public sealed record ServiceFieldDef(string Key, string Label, ServiceFieldKind Kind);

/// <summary>
/// A repeatable group of rows attached to every record of a service type - the
/// "VPS Systems Informations" shape from the simulation workbook.
/// </summary>
/// <param name="Key">The stable identity of the table, as <see cref="ServiceFieldDef.Key"/>.</param>
/// <param name="Label">What the user calls the table - "System information".</param>
/// <param name="Fields">The columns of each row, in the user's order.</param>
public sealed record ServiceTableDef(
    string Key, string Label, IReadOnlyList<ServiceFieldDef> Fields);

/// <summary>
/// The structure of one service type: which fields its records carry, and which
/// tables of extra rows ride along with each record.
/// </summary>
/// <remarks>
/// One row per service type, keyed by the same id as its <see cref="Service"/> row.
/// The structure is stored as JSON rather than real tables: a table per type would
/// mean DDL at runtime, which the encrypted store's whole EF pipeline cannot see,
/// while JSON keeps the definition and the records inside the same encrypted file
/// with one source of truth for what a record means.
///
/// <see cref="ConfiguredAt"/> is the designer's footprint. A type with no definition
/// row, or one that has never been saved, opens into the designer rather than into
/// its records - which is what "the first time you open it" means.
///</remarks>
public sealed class ServiceDefinition
{
    /// <summary>The service type this structure belongs to. Mirrors <see cref="Service.Id"/>.</summary>
    public int Id { get; set; }

    /// <summary>The fields, serialized. <see cref="Fields"/> is the one to use.</summary>
    public string FieldsJson { get; set; } = "[]";

    /// <summary>The tables, serialized. <see cref="Tables"/> is the one to use.</summary>
    public string TablesJson { get; set; } = "[]";

    /// <summary>When the structure was first saved. Null until the type is set up.</summary>
    public DateTimeOffset? ConfiguredAt { get; set; }

    /// <summary>The fields in the user's order.</summary>
    [NotMapped]
    public IReadOnlyList<ServiceFieldDef> Fields => ParseFields(FieldsJson);

    /// <summary>The tables in the user's order.</summary>
    [NotMapped]
    public IReadOnlyList<ServiceTableDef> Tables => ParseTables(TablesJson);

    /// <summary>True once the structure has been designed and saved at least once.</summary>
    [NotMapped]
    public bool IsConfigured => ConfiguredAt is not null;

    /// <summary>The first text field, which names every record of this type.</summary>
    [NotMapped]
    public ServiceFieldDef? NameField
    {
        get
        {
            foreach (ServiceFieldDef field in Fields)
            {
                if (field.Kind == ServiceFieldKind.Text)
                {
                    return field;
                }
            }

            return null;
        }
    }

    /// <summary>Serializes fields for storage.</summary>
    public static string PackFields(IReadOnlyList<ServiceFieldDef> fields)
        => JsonSerializer.Serialize(fields, Options);

    /// <summary>Serializes tables for storage.</summary>
    public static string PackTables(IReadOnlyList<ServiceTableDef> tables)
        => JsonSerializer.Serialize(tables, Options);

    private static IReadOnlyList<ServiceFieldDef> ParseFields(string json)
        => Deserialize<ServiceFieldDef>(json);

    private static IReadOnlyList<ServiceTableDef> ParseTables(string json)
        => Deserialize<ServiceTableDef>(json);

    private static IReadOnlyList<T> Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<T>>(json, Options)
                ?? Array.Empty<T>();
        }
        catch (JsonException)
        {
            // A structure this app cannot read is worse than no structure: return
            // nothing and let the caller offer the designer, rather than throwing
            // inside a converter that a page read depends on.
            return Array.Empty<T>();
        }
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        // Kinds as names, so a future kind renumbering cannot corrupt an old vault.
        Converters = { new JsonStringEnumConverter() },
    };
}
