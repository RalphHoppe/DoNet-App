using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Data;

namespace DoNet.Contracts;

/// <summary>
/// The one connection to the encrypted database, shared by everything that reads it.
/// </summary>
/// <remarks>
/// <para>
/// This exists because there must be exactly one. SQLCipher derives the file key with
/// 256,000 rounds of PBKDF2-HMAC-SHA512 on every open, so a second directory opening
/// its own connection would pay that a second time on every unlock, hold a second
/// handle on the file, and put two writers on one write-ahead log. One connection,
/// opened once, shared by every store that needs it.
/// </para>
/// <para>
/// Access is serialised: a SqliteConnection is not safe for concurrent use, and the
/// directories genuinely do overlap - the welcome screen's warm-up can still be
/// running when the first page is requested.
/// </para>
/// </remarks>
public interface IEncryptedStore
{
    /// <summary>
    /// Readies what does not need the key: the native library and EF's object model.
    /// Safe before unlock, and safe never to call.
    /// </summary>
    Task PrepareAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens and keys the connection if it is not already open.</summary>
    Task WarmUpAsync(CancellationToken cancellationToken = default);

    /// <summary>Closes the connection and drops the key held by it.</summary>
    Task CloseAsync();

    /// <summary>
    /// Runs one unit of work against the database with the connection held
    /// exclusively, on a thread pool thread.
    /// </summary>
    /// <param name="work">The work. Receives a context valid only for its duration.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <param name="operation">
    /// Supplied by the compiler, so a slow-operation warning names the caller without
    /// every call site passing a string.
    /// </param>
    Task<T> RunAsync<T>(
        Func<DoNetDbContext, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default,
        [CallerMemberName] string operation = "");
}
