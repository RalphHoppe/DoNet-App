using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Data;
using Microsoft.EntityFrameworkCore;

namespace DoNet.Services;

/// <summary>
/// Adds tables the model expects but the open database does not have.
/// </summary>
/// <remarks>
/// <para>
/// EnsureCreated creates the whole schema when the file is new and does nothing at
/// all when the file already exists. It has no opinion about a database that exists
/// but is a version behind. So the first run after the Websites directory shipped
/// opened a database created before those tables existed, every query against them
/// failed, and the directory showed its error state. Erasing the database fixed it,
/// which is not a repair anybody should have to find.
/// </para>
/// <para>
/// This closes that gap without taking on migrations. The DDL comes from the model
/// through GenerateCreateScript, so there is still one source of truth and no
/// hand-written schema to fall out of step; the statements are only made idempotent
/// and replayed. It cannot rename a column or change a type - when the schema needs
/// that, this is the thing to replace with real migrations.
/// </para>
/// </remarks>
internal static class SchemaGuard
{
    /// <summary>
    /// Creates any table in the model that the database is missing.
    /// </summary>
    /// <param name="db">A context over an already open, already keyed connection.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The names of the tables this call created, in model order.</returns>
    /// <remarks>
    /// The return value is the only trustworthy "this database has never had this
    /// table" signal, which is what first-run seeding keys off. An empty table
    /// afterwards is ambiguous - the user may have emptied it on purpose.
    /// </remarks>
    internal static async Task<List<string>> EnsureTablesAsync(
        DoNetDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        List<string> expected = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        HashSet<string> present = await ReadTableNamesAsync(db, cancellationToken).ConfigureAwait(false);
        List<string> missing = expected.Where(name => !present.Contains(name)).ToList();

        if (missing.Count == 0)
        {
            // The common path, and it costs one cheap read against sqlite_master.
            return missing;
        }

        AppLog.Info(
            $"The database is missing {missing.Count} table(s) the model expects: " +
            $"{string.Join(", ", missing)}. Creating them.");

        string script = db.Database.GenerateCreateScript();
        int applied = 0;
        List<string> created = new();

        // The generated DDL for this model contains no string literals, so splitting
        // on the statement terminator is safe here. Anything that is not a CREATE is
        // left alone and logged rather than guessed at.
        foreach (string piece in script.Split(';'))
        {
            string statement = piece.Trim();
            if (statement.Length == 0)
            {
                continue;
            }

            if (!statement.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Warn($"Schema guard skipped a statement it does not handle: {Summarise(statement)}");
                continue;
            }

            // A created table is a table the database has never had, which is what
            // the caller's first-run seeding keys off. The name is read back out of
            // the statement rather than assumed, so the two can never disagree about
            // which table a CREATE was for.
            if (TableNameOf(statement) is { } table
                && missing.Contains(table)
                && !created.Contains(table))
            {
                created.Add(table);
            }

            await db.Database
                .ExecuteSqlRawAsync(MakeIdempotent(statement), cancellationToken)
                .ConfigureAwait(false);
            applied++;
        }

        AppLog.Info($"Schema guard applied {applied} statement(s).");

        return created;
    }

    /// <summary>Reads the table name out of a CREATE TABLE statement, or null.</summary>
    private static string? TableNameOf(string statement)
    {
        // The shape is "CREATE TABLE [IF NOT EXISTS] \"Name\" (". The quoted form is
        // what GenerateCreateScript emits; the bare form is accepted in case that
        // ever changes, and anything else is somebody else's statement.
        string body = statement;

        int table = body.IndexOf("TABLE ", StringComparison.OrdinalIgnoreCase);
        if (table < 0)
        {
            return null;
        }

        body = body[(table + "TABLE ".Length)..].TrimStart();

        if (body.StartsWith("IF NOT EXISTS", StringComparison.OrdinalIgnoreCase))
        {
            body = body["IF NOT EXISTS".Length..].TrimStart();
        }

        if (body.StartsWith('"'))
        {
            int close = body.IndexOf('"', 1);
            return close > 1 ? body[1..close] : null;
        }

        int end = body.IndexOfAny(new[] { ' ', '(' });
        return end > 0 ? body[..end] : null;
    }

    private static async Task<HashSet<string>> ReadTableNamesAsync(
        DoNetDbContext db, CancellationToken cancellationToken)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";

        await using DbDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>
    /// Rewrites a CREATE so replaying it over an existing object is harmless.
    /// </summary>
    private static string MakeIdempotent(string statement)
    {
        foreach (string prefix in new[] { "CREATE TABLE ", "CREATE UNIQUE INDEX ", "CREATE INDEX " })
        {
            if (statement.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat(prefix, "IF NOT EXISTS ", statement.AsSpan(prefix.Length));
            }
        }

        return statement;
    }

    private static string Summarise(string statement)
    {
        string line = statement.ReplaceLineEndings(" ");
        return line.Length <= 80 ? line : string.Concat(line.AsSpan(0, 77), "...");
    }
}
