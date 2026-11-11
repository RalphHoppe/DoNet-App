using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Data;
using DoNet.Models;

namespace DoNet.Services;

/// <summary>
/// Writes the built-in service types into a Services table that has just been
/// created.
/// </summary>
/// <remarks>
/// Seeded once, at the moment the table comes into existence - never on every open.
/// The difference matters: a user who deletes VPS must not have it resurrected by
/// the next unlock, which is what seeding an empty table would do. The store knows
/// which case it is looking at because <see cref="SchemaGuard"/> reports the tables
/// it had to create, and "this table did not exist a moment ago" is the one honest
/// signal for "this database has never had a catalog".
///</remarks>
internal static class ServiceCatalogSeed
{
    /// <param name="db">A context over an already open, already keyed connection.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    internal static async Task SeedAsync(DoNetDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        // The six, in the workbook's order, with nothing else filled in: the
        // description and note columns exist to be written by the user, not by us.
        db.Services.AddRange(ServiceCatalog.BuiltIn
            .Select(name => new Service { Name = name })
            .ToList());

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        AppLog.Info($"Seeded the service catalog with {ServiceCatalog.BuiltIn.Count} types.");
    }
}
