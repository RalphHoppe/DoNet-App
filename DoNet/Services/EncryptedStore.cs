using System;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Contracts;
using DoNet.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DoNet.Services;

/// <summary>
/// Owns the single keyed connection to the encrypted database.
/// </summary>
/// <remarks>
/// <para>
/// One connection, opened once and kept open until the app locks. This is the most
/// important thing about this class. SQLCipher derives the file key with 256,000
/// rounds of PBKDF2-HMAC-SHA512 on every <c>PRAGMA key</c>, which is deliberate and
/// costs a visible fraction of a second. Opening per operation pays it per operation -
/// and with a search box that queries as you type, per keystroke.
/// </para>
/// <para>
/// Short-lived contexts over a long-lived connection: the change tracker stays clean
/// without re-deriving the key.
/// </para>
/// </remarks>
public sealed class EncryptedStore : IEncryptedStore, IDisposable
{
    /// <summary>Operations slower than this are written to the log.</summary>
    /// <remarks>
    /// Only the outliers. A file append on the path of every keystroke is the kind of
    /// instrumentation that becomes the performance problem. 150ms is roughly nine
    /// frames - past the point where a person notices the UI waiting.
    /// </remarks>
    private const int SlowOperationMs = 150;

    /// <summary>
    /// SQLITE_NOTADB. Under SQLCipher this almost always means the key is wrong rather
    /// than the file being damaged: every page including the header is encrypted, so a
    /// wrong key makes the whole file look like noise.
    /// </summary>
    private const int NotADatabase = 26;

    /// <summary>
    /// Loads the SQLCipher native library, once, on whichever thread needs it first.
    /// </summary>
    /// <remarks>
    /// The .Core EF package does not do this for us, and without it the first
    /// connection fails with "You need to call SQLitePCL.raw.SetProvider()". Lazy so
    /// the native load happens on the thread pool behind the splash, not between
    /// process start and the first pixel.
    /// </remarks>
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

    public EncryptedStore(IVaultService vault)
    {
        _vault = vault;
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
                _ = NativeProvider.Value;

                // An in-memory source that is never opened. Building the model touches
                // no file and needs no key - EF only reflects over the entity types -
                // so this is safe before the user has seen the lock screen, and the
                // model it caches is the one the real contexts will use.
                using SqliteConnection probe = new("Data Source=:memory:");
                using DoNetDbContext model = new(probe);

                _ = model.Model;
            },
            cancellationToken).ConfigureAwait(false);

        AppLog.Info(
            $"Data layer prepared in {Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0} ms");
    }

    public async Task WarmUpAsync(CancellationToken cancellationToken = default)
    {
        // Only meaningful after an unlock. Failing here would turn a timing detail
        // into a visible error.
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

    public async Task<T> RunAsync<T>(
        Func<DoNetDbContext, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string operation = "")
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);

        long startedAt = Stopwatch.GetTimestamp();

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SqliteConnection connection = await EnsureOpenAsync(cancellationToken).ConfigureAwait(false);

            // SQLite has no real asynchronous I/O - its "async" EF methods complete
            // synchronously on the calling thread - so without this every query runs
            // on whichever thread called it, which for a search box is the UI thread.
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

    public async Task CloseAsync()
    {
        if (_disposed)
        {
            return;
        }

        // Takes the gate first. Disposing the connection while a query is running on
        // it is a native-level race - the managed object goes away underneath a
        // sqlite3 handle that is mid-statement - and the result is an access violation
        // rather than a catchable exception.
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _connection?.Dispose();
            _connection = null;

            // No-op while pooling is off, and the one line that would stop a pooled,
            // still-keyed handle outliving the lock screen if it is ever turned on.
            SqliteConnection.ClearAllPools();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Opens and keys the connection if it is not already open. Callers hold the gate.
    /// </summary>
    /// <remarks>
    /// The open runs on a thread pool thread because <see cref="SqliteConnection.Open"/>
    /// is synchronous and derives the key inline. Called from the welcome screen
    /// without this, it would block the UI thread for the length of the PBKDF2 run and
    /// stall the animation playing at the time.
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
                    // which is what engages SQLCipher. Without it the same provider
                    // silently creates a perfectly readable database.
                    Password = key,

                    // Irrelevant now the connection is long-lived, and off so a pooled
                    // handle cannot survive a lock still keyed.
                    Pooling = false,
                };

                _ = NativeProvider.Value;

                SqliteConnection opened = new(builder.ToString());

                try
                {
                    opened.Open();

                    using SqliteCommand pragma = opened.CreateCommand();

                    // WAL lets reads proceed without blocking, and NORMAL avoids an
                    // fsync per commit while staying crash-safe under WAL.
                    pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
                    pragma.ExecuteNonQuery();
                }
                catch (Exception error)
                {
                    // A failed Open still holds the file. SQLite opens it, reads the
                    // header, rejects it, and the wrapper keeps the handle until
                    // something disposes it - and a locked database is a file the
                    // password reset cannot delete, so the one action that recovers
                    // from an unreadable store would be blocked by the failed attempt
                    // to read it.
                    opened.Dispose();

                    if (error is SqliteException { SqliteErrorCode: NotADatabase })
                    {
                        AppLog.Error(
                            "The database exists but this key cannot decrypt it. That means "
                            + "it was left behind by an earlier password - resetting the "
                            + "password removes it and starts clean.",
                            error);
                    }

                    throw;
                }

                return opened;
            },
            cancellationToken).ConfigureAwait(false);

        _connection = connection;

        // Worth a line every time. This is the one unavoidably expensive thing the app
        // does, and the real number from a real machine is what says whether a
        // complaint about slowness is this or something else.
        AppLog.Info(
            $"Database opened and keyed in {Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0} ms");

        // EnsureCreated rather than migrations: the schema has one version so far, and
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
    /// Shutdown. Closes the database cleanly if it can do so safely.
    /// </summary>
    /// <remarks>
    /// Dispose cannot be asynchronous, so it waits briefly rather than awaiting. In
    /// practice the gate is free immediately; the wait is for when it is not.
    ///
    /// On timeout the connection is deliberately left alone. Disposing it mid-statement
    /// is a native race, and an access violation as the window closes is still a crash.
    /// Letting the process end costs nothing: the OS closes the handle and the
    /// write-ahead log is recovered on the next open, which is what WAL is for.
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
}
