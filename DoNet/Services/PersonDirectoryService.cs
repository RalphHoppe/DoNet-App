using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Contracts;
using DoNet.Data;
using DoNet.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DoNet.Services;

/// <summary>
/// The person store, backed by an encrypted SQLite database.
/// </summary>
/// <remarks>
/// <para>
/// One connection, opened once and kept open until the app locks. This is the single
/// most important thing about this class. SQLCipher derives the file key with 256,000
/// rounds of PBKDF2-HMAC-SHA512 on every <c>PRAGMA key</c>, which is deliberate and
/// costs a visible fraction of a second. Opening a connection per operation therefore
/// pays that cost per operation - and with a search box that queries as you type, per
/// keystroke, twice. Zetetic's own guidance is explicit: reuse the connection, open
/// once at startup, and subsequent access on the same handle skips key derivation
/// entirely.
/// </para>
/// <para>
/// Because one connection is shared, every operation is serialised through
/// <see cref="_gate"/>. A SqliteConnection is not safe for concurrent use, and the
/// warm-up from the welcome screen genuinely can overlap the directory's first page.
/// </para>
/// </remarks>
public sealed class PersonDirectoryService : IPersonDirectory, IDisposable
{
    /// <summary>
    /// Loads the SQLCipher native library, once, on whichever thread needs it first.
    /// </summary>
    /// <remarks>
    /// The .Core EF package does not call this for us the way the all-in-one package
    /// would, and without it the first connection fails with "You need to call
    /// SQLitePCL.raw.SetProvider()". It used to run in the App constructor, which put a
    /// native DLL load on the path between process start and the first pixel. Here it
    /// happens on the thread pool, behind the splash, and only if the database is
    /// actually opened.
    /// </remarks>
    /// <summary>
    /// Operations slower than this get written to the log.
    /// </summary>
    /// <remarks>
    /// Only the outliers. Logging every query would put a file append on the path of
    /// every keystroke, which is the kind of instrumentation that becomes the
    /// performance problem. 150ms is roughly nine frames - comfortably past the point
    /// where a person notices the UI waiting.
    /// </remarks>
    private const int SlowOperationMs = 150;

    private static readonly Lazy<bool> NativeProvider = new(
        () =>
        {
            SQLitePCL.Batteries_V2.Init();
            return true;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IVaultService _vault;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SqliteConnection? _connection;
    private bool _disposed;

    public PersonDirectoryService(IVaultService vault)
    {
        _vault = vault;
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        // Called from the welcome screen, which only runs after a successful unlock.
        // If the key is not there yet there is nothing useful to do, and failing here
        // would turn a timing detail into a visible error.
        if (_disposed || !_vault.IsUnlocked)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PrepareAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return;
        }

        long startedAt = Stopwatch.GetTimestamp();

        await Task.Run(
            () =>
            {
                // Loads the SQLCipher native library.
                _ = NativeProvider.Value;

                // An in-memory source that is never opened. Building the model touches
                // no file and needs no key - EF only reflects over the entity types -
                // so this is safe to run before the user has even seen the lock screen,
                // and the model it caches is the one the real contexts will use.
                using SqliteConnection probe = new("Data Source=:memory:");
                using DoNetDbContext model = new(probe);

                _ = model.Model;
            },
            cancellationToken).ConfigureAwait(false);

        AppLog.Info(
            "Data layer prepared in "
            + $"{Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0} ms");
    }

    /// <summary>
    /// Opens and keys the connection if it is not already open.
    /// </summary>
    /// <remarks>
    /// The open runs on a thread pool thread because <see cref="SqliteConnection.Open"/>
    /// is synchronous and does the key derivation inline. Called from the welcome
    /// screen without this, it would block the UI thread for the length of the PBKDF2
    /// run and visibly stall the animation that is playing at the time.
    /// </remarks>
    private async Task<SqliteConnection> EnsureOpenAsync(CancellationToken cancellationToken)
    {
        if (_connection is { State: ConnectionState.Open })
        {
            return _connection;
        }

        _connection?.Dispose();
        _connection = null;

        string path = AppPaths.DatabasePath;
        string key = _vault.DatabaseKey;

        long startedAt = Stopwatch.GetTimestamp();

        SqliteConnection connection = await Task.Run(
            () =>
            {
                SqliteConnectionStringBuilder builder = new()
                {
                    DataSource = path,
                    Mode = SqliteOpenMode.ReadWriteCreate,

                    // Password makes Microsoft.Data.Sqlite issue PRAGMA key on open,
                    // which is what actually engages SQLCipher. Without it the same
                    // provider silently creates a perfectly readable database.
                    Password = key,

                    // Irrelevant now that the connection is long-lived, and off so a
                    // pooled handle cannot survive Lock() still keyed.
                    Pooling = false,
                };

                _ = NativeProvider.Value;

                SqliteConnection opened = new(builder.ToString());
                opened.Open();

                using SqliteCommand pragma = opened.CreateCommand();

                // WAL lets reads proceed without blocking, and NORMAL avoids an fsync
                // per commit while remaining crash-safe under WAL.
                pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                pragma.ExecuteNonQuery();

                return opened;
            },
            cancellationToken).ConfigureAwait(false);

        _connection = connection;

        // Worth a line in the log every time. This is the one unavoidably expensive
        // thing the app does - 256,000 rounds of PBKDF2 - and having the real number
        // from a real machine is what tells us whether a complaint about slowness is
        // this or something else.
        AppLog.Info(
            "Database opened and keyed in "
            + $"{Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0} ms");

        // EnsureCreated rather than migrations: there is one schema version so far, and
        // migrations would add a toolchain for a problem that does not exist yet. This
        // is the line that changes when the schema first evolves.
        await Task.Run(
            async () =>
            {
                await using DoNetDbContext db = new(connection);
                await db.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return connection;
    }

    /// <summary>
    /// Runs one unit of database work with the shared connection held exclusively.
    /// </summary>
    /// <remarks>
    /// The body runs on a thread pool thread. SQLite has no real asynchronous I/O - its
    /// "async" EF methods complete synchronously on the calling thread - so without this
    /// every query would execute on whichever thread called it, which for a search box
    /// is the UI thread.
    /// </remarks>
    /// <param name="operation">
    /// Filled in by the compiler with the calling method's name, so a slow-operation
    /// warning says which one was slow without every call site having to pass a string.
    /// </param>
    private async Task<T> WithDatabaseAsync<T>(
        Func<DoNetDbContext, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken,
        [CallerMemberName] string operation = "")
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long startedAt = Stopwatch.GetTimestamp();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SqliteConnection connection = await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

            return await Task.Run(
                async () =>
                {
                    await using DoNetDbContext db = new(connection);
                    return await work(db, cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();

            double elapsed = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

            if (elapsed >= SlowOperationMs)
            {
                AppLog.Warn($"{operation} took {elapsed:F0} ms");
            }
        }
    }

    public Task<IReadOnlyList<Person>> GetPageAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => WithDatabaseAsync<IReadOnlyList<Person>>(
            async (db, token) => await Filter(db.People.AsNoTracking(), search)
                .OrderByDescending(p => p.Id)
                .Skip(skip)
                .Take(take)
                .ToListAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    public Task<int> CountAsync(
        string? search = null, CancellationToken cancellationToken = default)
        => WithDatabaseAsync(
            async (db, token) => await Filter(db.People.AsNoTracking(), search)
                .CountAsync(token)
                .ConfigureAwait(false),
            cancellationToken);

    /// <summary>
    /// One page and its total in a single trip, which is what the directory actually
    /// needs. Two calls meant taking the gate twice for one screen refresh.
    /// </summary>
    public Task<(IReadOnlyList<Person> Page, int Total)> GetPageWithTotalAsync(
        int skip, int take, string? search = null, CancellationToken cancellationToken = default)
        => WithDatabaseAsync<(IReadOnlyList<Person> Page, int Total)>(
            async (db, token) =>
            {
                IQueryable<Person> filtered = Filter(db.People.AsNoTracking(), search);

                int total = await filtered.CountAsync(token).ConfigureAwait(false);

                List<Person> page = await filtered
                    .OrderByDescending(p => p.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                return ((IReadOnlyList<Person>)page, total);
            },
            cancellationToken);

    public Task<Person> AddAsync(Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        return WithDatabaseAsync(
            async (db, token) =>
            {
                Person stored = person.Clone();
                stored.Id = 0;                          // the database assigns it
                stored.CreatedAt = DateTimeOffset.Now;

                db.People.Add(stored);
                await db.SaveChangesAsync(token).ConfigureAwait(false);

                return stored;
            },
            cancellationToken);
    }

    public async Task UpdateAsync(Person person, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(person);

        await WithDatabaseAsync(
            async (db, token) =>
            {
                Person? existing = await db.People
                    .FirstOrDefaultAsync(p => p.Id == person.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    return false;
                }

                // Id and CreatedAt belong to the store, not to the form that edited it.
                existing.FirstName = person.FirstName;
                existing.LastName = person.LastName;
                existing.Gender = person.Gender;
                existing.DateOfBirth = person.DateOfBirth;
                existing.Country = person.Country;
                existing.State = person.State;
                existing.City = person.City;
                existing.Street = person.Street;
                existing.PostalCode = person.PostalCode;
                existing.PhoneNumber = person.PhoneNumber;
                existing.Email = person.Email;
                existing.EmailPassword = person.EmailPassword;
                existing.RecoveryEmail = person.RecoveryEmail;
                existing.RecoveryPassword = person.RecoveryPassword;
                existing.RecoveryWords = person.RecoveryWords;
                existing.Note = person.Note;

                await db.SaveChangesAsync(token).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
        => await WithDatabaseAsync(
            async (db, token) =>
            {
                Person? existing = await db.People
                    .FirstOrDefaultAsync(p => p.Id == id, token)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    db.People.Remove(existing);
                    await db.SaveChangesAsync(token).ConfigureAwait(false);
                }

                return true;
            },
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Closes the database. Called when the app locks, so the next unlock re-keys.
    /// </summary>
    /// <remarks>
    /// Takes the gate first. Disposing the connection while a query is still running on
    /// it is a native-level race - the managed object goes away underneath a sqlite3
    /// handle that is mid-statement - and the result is an access violation rather than
    /// a catchable exception. Locking is asynchronous and the user has already been sent
    /// to the lock screen by the time it completes; the key is dropped separately and
    /// immediately, so nothing waits on this.
    /// </remarks>
    public async Task CloseAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Shutdown. Closes the database cleanly if it can do so safely.
    /// </summary>
    /// <remarks>
    /// Dispose cannot be asynchronous, so it waits briefly for whatever is running
    /// rather than awaiting it. In practice the gate is free immediately - queries here
    /// are measured in milliseconds - and the wait exists for the case where it is not.
    ///
    /// If the wait times out the connection is deliberately left alone. Disposing it
    /// while a statement is executing is a native-level race, and an access violation
    /// as the window closes is still a crash in the eyes of anyone watching. Letting
    /// the process end instead costs nothing: the OS closes the handle, and the
    /// write-ahead log is recovered on the next open, which is exactly what WAL is for.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        bool acquired = false;

        try
        {
            acquired = _gate.Wait(TimeSpan.FromSeconds(1));

            if (acquired)
            {
                _connection?.Dispose();
                _connection = null;
            }
            else
            {
                AppLog.Warn("Database still busy at shutdown; left it to the OS to close.");
            }
        }
        catch (Exception error)
        {
            AppLog.Error("Closing the database at shutdown failed", error);
        }
        finally
        {
            if (acquired)
            {
                _gate.Release();
            }

            _gate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Applies the search box to a query.
    /// </summary>
    /// <remarks>
    /// LIKE rather than string.Contains: EF translates Contains to instr(), which is
    /// case sensitive, and a directory search that misses "ahmed" because the record
    /// says "Ahmed" is broken. SQLite's LIKE is case insensitive for ASCII.
    ///
    /// Secrets are not searched. A search box that matches password fields is a way to
    /// confirm a password by guessing at it one character at a time.
    /// </remarks>
    private static IQueryable<Person> Filter(IQueryable<Person> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        string term = search.Trim();

        // % and _ are wildcards; a user typing them means the literal character.
        string escaped = term
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        string pattern = $"%{escaped}%";

        bool numeric = int.TryParse(term, out int id);

        return query.Where(p =>
            (numeric && p.Id == id)
            || EF.Functions.Like(p.FirstName, pattern, "\\")
            || EF.Functions.Like(p.LastName, pattern, "\\")
            || EF.Functions.Like(p.Email, pattern, "\\")
            || EF.Functions.Like(p.Country, pattern, "\\")
            || EF.Functions.Like(p.City, pattern, "\\")
            || EF.Functions.Like(p.State, pattern, "\\")
            || EF.Functions.Like(p.PhoneNumber, pattern, "\\")
            || EF.Functions.Like(p.Note, pattern, "\\"));
    }
}
