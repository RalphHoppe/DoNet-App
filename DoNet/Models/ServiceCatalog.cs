using System.Collections.Generic;

namespace DoNet.Models;

/// <summary>
/// The service types the app ships knowing about.
/// </summary>
/// <remarks>
/// This list exists for one moment: seeding a brand-new database, or an existing
/// one the moment it grows a Services table. From then on the table is the catalog
/// and this list is not consulted again - unlike the payment methods, whose
/// built-ins live in code and are merged into every read, a service type carries a
/// description and a note of its own, so it has to be a row the moment it exists at
/// all. The difference shows up in deletion: deleting PayPal is remembered as a
/// hiding, while deleting VPS simply removes the row.
///</remarks>
public static class ServiceCatalog
{
    /// <summary>
    /// The six types the simulation workbook names, in its order. Description and
    /// note start empty - the workbook left them empty, and they are the user's to
    /// write.
    /// </summary>
    public static IReadOnlyList<string> BuiltIn { get; } = new[]
    {
        "VPS", "Domain", "SMS", "Proxy", "Phone", "Cloud",
    };
}
