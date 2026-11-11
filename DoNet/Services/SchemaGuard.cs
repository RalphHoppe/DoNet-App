using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
    /// Brings the open database up to the model: tables it is missing, columns
    /// its tables are missing, and indexes it is missing.
    /// </summary>
    /// <param name="db">A context over an already open, already keyed connection.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The names of the tables this call created, in model order.</returns>
    /// <remarks>
    /// <para>
    /// The return value is the only trustworthy "this database has never had this
    /// table" signal, which is what first-run seeding keys off. An empty table
    /// afterwards is ambiguous - the user may have emptied it on purpose.
    /// </para>
    /// <para>
    /// Columns are the same gap one level down: a table that shipped in an
    /// earlier round is present, and the model growing a column on it is
    /// invisible to a table-only pass - every query against that column would
    /// fail on the databases that were there before it. So each present
    /// table's columns are read and compared too, and the missing ones added.
    /// </para>
    /// </remarks>
    internal static async Task<List<string>> EnsureTablesAsync(
        DoNetDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);

        string script = db.Database.GenerateCreateScript();

        List<(string Table, string Column)> missingColumns = new();

        foreach (IGrouping<string, IEntityType> tableGroup in db.Model.GetEntityTypes()
                     .GroupBy(entity => entity.GetTableName() ?? string.Empty, StringComparer.Ordinal))
        {
            string tableName = tableGroup.Key;
            if (tableName.Length == 0)
            {
                continue;
            }

            IEntityType table = tableGroup.First();
            HashSet<string> columns = await ReadColumnNamesAsync(db, tableName, cancellationToken)
                .ConfigureAwait(false);

            // A table sqlite_master has not heard of is the table pass's to
            // create whole; the column pass is only for tables that exist.
            if (columns.Count == 0)
            {
                continue;
            }

            StoreObjectIdentifier store = StoreObjectIdentifier.Table(tableName, table.GetSchema());

            foreach (IProperty property in table.GetProperties())
            {
                string? column = property.GetColumnName(store);
                if (column is null || columns.Contains(column))
                {
                    continue;
                }

                missingColumns.Add((tableName, column));
            }
        }

        List<string> expectedTables = db.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        HashSet<string> presentTables =
            await ReadTableNamesAsync(db, cancellationToken).ConfigureAwait(false);
        List<string> missingTables = expectedTables
            .Where(name => !presentTables.Contains(name))
            .ToList();

        // The script's own index names, against what the database has. Indexes
        // ride along with the replayed script, so this is only the signal for
        // whether a replay has to happen at all.
        HashSet<string> presentIndexes =
            await ReadIndexNamesAsync(db, cancellationToken).ConfigureAwait(false);
        List<string> missingIndexes = IndexNamesOf(script)
            .Where(name => !presentIndexes.Contains(name))
            .ToList();

        if (missingTables.Count == 0 && missingColumns.Count == 0 && missingIndexes.Count == 0)
        {
            // The common path, and it costs a few cheap reads against sqlite_master.
            return missingTables;
        }

        AppLog.Info(
            $"Schema guard: {missingTables.Count} missing table(s), " +
            $"{missingColumns.Count} missing column(s), {missingIndexes.Count} missing index(es).");

        // The replayed script creates the missing tables whole, with all their
        // columns and indexes, and adds any index a new column brought with it;
        // the IF NOT EXISTS rewrites make every other statement a no-op.
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
                && missingTables.Contains(table)
                && !created.Contains(table))
            {
                created.Add(table);
            }

            await db.Database
                .ExecuteSqlRawAsync(MakeIdempotent(statement), cancellationToken)
                .ConfigureAwait(false);
            applied++;
        }

        // The columns the replay cannot add: they belong to tables that already
        // exist, so each becomes its own ALTER. SQLite can add a column that is
        // nullable and carries no default; one that is neither is left with a
        // warning rather than a half-guessed default.
        foreach ((string tableName, string column) in missingColumns)
        {
            IProperty property = PropertyOf(db, tableName, column);

            if (!property.IsNullable)
            {
                AppLog.Warn(
                    $"Schema guard cannot add the required column {tableName}.{column}; " +
                    "it needs a default or a migration.");
                continue;
            }

            string type = property.GetColumnType() ?? "TEXT";

            await db.Database.ExecuteSqlRawAsync(
                $"ALTER TABLE \"{tableName}\" ADD COLUMN \"{column}\" {type} NULL;",
                cancellationToken).ConfigureAwait(false);
            applied++;

            AppLog.Info($"Schema guard added column {tableName}.{column} ({type}).");
        }

        AppLog.Info($"Schema guard applied {applied} statement(s).");

        return created;
    }

    /// <summary>The model property behind one table's column.</summary>
    /// <remarks>
    /// The pair comes from the model itself moments earlier, so the lookup only
    /// fails if the model changed under it, and a failure inside a schema
    /// repair should stop the repair rather than guess a type.
    /// </remarks>
    private static IProperty PropertyOf(DoNetDbContext db, string tableName, string column)
    {
        IEntityType table = db.Model.GetEntityTypes()
            .FirstOrDefault(entity => entity.GetTableName() == tableName)
            ?? throw new InvalidOperationException(
                $"The model has no table {tableName} to add a column to.");

        StoreObjectIdentifier store = StoreObjectIdentifier.Table(tableName, table.GetSchema());

        return table.GetProperties().FirstOrDefault(
                   property => property.GetColumnName(store) == column)
            ?? throw new InvalidOperationException(
                $"The model has no column {tableName}.{column} to add.");
    }

    /// <summary>The index names out of a creation script, in order.</summary>
    private static IEnumerable<string> IndexNamesOf(string script)
    {
        foreach (string piece in script.Split(';'))
        {
            string statement = piece.Trim();

            int index = statement.IndexOf("INDEX \"", StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            int start = index + "INDEX \"".Length;
            int end = statement.IndexOf('"', start);
            if (end > start)
            {
                yield return statement[start..end];
            }
        }
    }

    /// <summary>Reads one table's column names; empty when the table is not there.</summary>
    private static async Task<HashSet<string>> ReadColumnNamesAsync(
        DoNetDbContext db, string tableName, CancellationToken cancellationToken)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";

        await using DbDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    /// <summary>The index names the database already has.</summary>
    private static async Task<HashSet<string>> ReadIndexNamesAsync(
        DoNetDbContext db, CancellationToken cancellationToken)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

        DbConnection connection = db.Database.GetDbConnection();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index'";

        await using DbDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }

        return names;
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
