using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DoNet.Contracts;
using Konscious.Security.Cryptography;

namespace DoNet.Services;

/// <summary>
/// The master password, as stored on disk.
/// </summary>
/// <remarks>
/// <para>
/// The password is never written anywhere. Instead it derives a key-encryption key
/// through Argon2id, and that key wraps a random 32-byte data key with AES-256-GCM.
/// Unlocking means trying to unwrap: if the password is wrong the derived key is wrong,
/// the GCM authentication tag fails, and the attempt is rejected. There is nothing on
/// disk to compare a guess against, so there is no hash to crack offline beyond
/// replaying the full Argon2id cost for every guess.
/// </para>
/// <para>
/// The indirection through a data key is not decoration. It means the database can be
/// encrypted under a key that never changes, so changing the master password later only
/// has to re-wrap 32 bytes instead of re-encrypting everything.
/// </para>
/// </remarks>
public sealed class VaultService : IVaultService
{
    private const string FileName = "vault.json";

    private const int SaltBytes = 16;
    private const int KeyBytes = 32;    // AES-256
    private const int NonceBytes = 12;  // GCM standard
    private const int TagBytes = 16;    // GCM full-length tag

    // OWASP's floor for Argon2id is 19 MiB / t=2 / p=1. This is well above it, while
    // still finishing fast enough that the create and unlock screens stay responsive.
    private const int MemoryKiB = 65536;  // 64 MiB
    private const int Iterations = 3;
    private const int Parallelism = 4;

    private readonly Lazy<string> _vaultPath = new(ResolveVaultPath);

    /// <summary>
    /// The unwrapped data key, base64, held only while the app is unlocked.
    /// </summary>
    /// <remarks>
    /// A string rather than a byte array, which is a real compromise: strings cannot be
    /// zeroed and live until the GC collects them. It is what the connection string API
    /// takes, and the alternative - issuing PRAGMA key by hand on every connection -
    /// trades that for a different set of sharp edges. The key never reaches disk, and
    /// <see cref="Lock"/> drops it.
    /// </remarks>
    private string? _databaseKey;

    public bool IsUnlocked => _databaseKey is not null;

    /// <summary>
    /// The key the encrypted database is opened with. Only valid while unlocked.
    /// </summary>
    /// <exception cref="InvalidOperationException">The app is locked.</exception>
    public string DatabaseKey => _databaseKey
        ?? throw new InvalidOperationException(
            "The vault is locked. The database cannot be opened without unlocking first.");

    /// <summary>
    /// Drops the data key. After this the database cannot be read until the password is
    /// entered again, which is the entire point of the lock button.
    /// </summary>
    public void Lock() => _databaseKey = null;

    public bool IsInitialized
    {
        get
        {
            try
            {
                return File.Exists(_vaultPath.Value);
            }
            catch (Exception)
            {
                // Asked on the splash screen, where throwing would strand the user.
                // Reporting "not set up" sends them to onboarding, which is recoverable;
                // a lock screen with no vault behind it is not.
                return false;
            }
        }
    }

    public async Task CreateAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        if (IsInitialized)
        {
            throw new InvalidOperationException("A vault already exists for this installation.");
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var dataKey = RandomNumberGenerator.GetBytes(KeyBytes);
        var wrapped = new byte[KeyBytes];
        var tag = new byte[TagBytes];

        var keyEncryptionKey = await DeriveKeyAsync(
            password, salt, MemoryKiB, Iterations, Parallelism, cancellationToken).ConfigureAwait(false);

        try
        {
            using var aes = new AesGcm(keyEncryptionKey, TagBytes);
            aes.Encrypt(nonce, dataKey, wrapped, tag);

            // Captured before the finally wipes it. Creating a vault leaves the app
            // unlocked - the user goes straight to the home screen from here, and
            // without this the database would be unopenable until the first re-login.
            _databaseKey = Convert.ToBase64String(dataKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyEncryptionKey);
            CryptographicOperations.ZeroMemory(dataKey);
        }

        var descriptor = new VaultDescriptor
        {
            MemoryKiB = MemoryKiB,
            Iterations = Iterations,
            Parallelism = Parallelism,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            WrappedKey = Convert.ToBase64String(wrapped),
            Tag = Convert.ToBase64String(tag),
            CreatedUtc = DateTimeOffset.UtcNow.ToString("O"),
        };

        await WriteAsync(descriptor, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryUnlockAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password) || !IsInitialized)
        {
            return false;
        }

        VaultDescriptor descriptor;

        await using (var stream = File.OpenRead(_vaultPath.Value))
        {
            descriptor = await JsonSerializer
                .DeserializeAsync(stream, VaultJsonContext.Default.VaultDescriptor, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidDataException("The vault file is empty.");
        }

        if (descriptor.Version != 1 || !string.Equals(descriptor.Kdf, "argon2id", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported vault format (version {descriptor.Version}, kdf {descriptor.Kdf}).");
        }

        var salt = Convert.FromBase64String(descriptor.Salt);
        var nonce = Convert.FromBase64String(descriptor.Nonce);
        var wrapped = Convert.FromBase64String(descriptor.WrappedKey);
        var tag = Convert.FromBase64String(descriptor.Tag);

        // Parameters come from the file, not from the constants above, so a vault created
        // with older settings still opens after those settings are raised.
        var keyEncryptionKey = await DeriveKeyAsync(
            password, salt, descriptor.MemoryKiB, descriptor.Iterations, descriptor.Parallelism, cancellationToken)
            .ConfigureAwait(false);

        var dataKey = new byte[wrapped.Length];

        try
        {
            using var aes = new AesGcm(keyEncryptionKey, tag.Length);
            aes.Decrypt(nonce, wrapped, tag, dataKey);

            // The data key is correct here. Retained so the encrypted database can be
            // opened; dropped again by Lock().
            _databaseKey = Convert.ToBase64String(dataKey);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            // The one expected failure: wrong password. Anything else is a real fault
            // and is left to propagate.
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyEncryptionKey);
            CryptographicOperations.ZeroMemory(dataKey);
        }
    }

    /// <summary>
    /// Exercises the crypto path with throwaway inputs so a real unlock does not also
    /// pay to load and compile it.
    /// </summary>
    /// <remarks>
    /// Deliberately tiny parameters. The expense of Argon2id is memory-hardness, and
    /// none of that is wanted here - the only goal is to touch the same methods the
    /// real derivation will, so the assembly is loaded and the inner loop is already
    /// compiled. Running the real parameters would burn 64 MiB and three passes to
    /// save the same few tens of milliseconds.
    ///
    /// Nothing derived here is kept, and everything is wiped on the way out, same as
    /// the real path.
    /// </remarks>
    public Task WarmUpAsync(CancellationToken cancellationToken = default)
        => Task.Run(
            () =>
            {
                var probe = Encoding.UTF8.GetBytes("warm-up");

                try
                {
                    using var argon2 = new Argon2id(probe)
                    {
                        Salt = new byte[SaltBytes],
                        MemorySize = 1024,
                        Iterations = 1,
                        DegreeOfParallelism = 1,
                    };

                    var derived = argon2.GetBytes(KeyBytes);
                    CryptographicOperations.ZeroMemory(derived);

                    // The unwrap path too: AES-GCM runs on every unlock right after
                    // the derivation, and shares none of its code.
                    var key = new byte[KeyBytes];
                    var nonce = new byte[NonceBytes];
                    var tag = new byte[TagBytes];
                    var plain = new byte[KeyBytes];
                    var cipher = new byte[KeyBytes];

                    using var aes = new AesGcm(key, TagBytes);
                    aes.Encrypt(nonce, plain, cipher, tag);

                    CryptographicOperations.ZeroMemory(key);
                    CryptographicOperations.ZeroMemory(plain);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(probe);
                }
            },
            cancellationToken);

    /// <summary>
    /// Erases the vault and the database, returning the app to a first run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The database has to go with the vault, and this is not housekeeping - it is the
    /// difference between the reset working and the app being permanently broken.
    /// <see cref="CreateAsync"/> generates a brand new random data key, so a database
    /// left behind from the previous password is encrypted with a key that no longer
    /// exists anywhere. SQLCipher cannot open it, and never will be able to, so every
    /// launch from then on lands on "The directory could not be opened".
    /// </para>
    /// <para>
    /// Nothing is lost that was not already lost. The master password is the only way
    /// to unwrap the data key and it is never stored, so by the time someone reaches
    /// this screen the records are unreadable by us as much as by anyone else. The
    /// screen says exactly that before offering the button.
    /// </para>
    /// </remarks>
    /// <exception cref="IOException">
    /// A file could not be removed - usually because a connection to the database is
    /// still open. Thrown rather than swallowed: a reset that half-succeeds leaves the
    /// user in the broken state this method exists to prevent, and they need to know
    /// now rather than at the next launch.
    /// </exception>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        // The old key cannot open anything after the vault is gone.
        _databaseKey = null;

        var vault = _vaultPath.Value;
        var folder = Path.GetDirectoryName(vault);

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            // Nothing was ever written, so there is nothing to erase.
            return;
        }

        var failures = new List<string>();

        foreach (var path in Targets(vault))
        {
            if (!await TryDeleteAsync(path, cancellationToken).ConfigureAwait(false))
            {
                failures.Add(Path.GetFileName(path));
            }
        }

        if (failures.Count > 0)
        {
            throw new IOException(
                "Some files could not be removed: "
                + string.Join(", ", failures)
                + ". Close the app and try again.");
        }
    }

    /// <summary>
    /// Deletes a file, retrying briefly if Windows says it is in use.
    /// </summary>
    /// <remarks>
    /// Deleting a file that is not there is not an error, so there is no existence
    /// check - only the directory had to be confirmed by the caller.
    ///
    /// The retries are for the gap between a handle being released and Windows
    /// agreeing that it has been. A database connection that has just been disposed,
    /// an indexer, a virus scanner reading the file it watched appear: all of them
    /// produce a sharing violation that is gone a moment later. Failing a destructive
    /// one-shot operation on a hundred-millisecond race would leave the user with a
    /// half-erased vault and no obvious way forward.
    /// </remarks>
    private static async Task<bool> TryDeleteAsync(string path, CancellationToken cancellationToken)
    {
        const int attempts = 4;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                if (attempt == attempts)
                {
                    AppLog.Error($"Could not delete '{Path.GetFileName(path)}' during reset", error);
                    return false;
                }

                await Task.Delay(120 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }

    /// <summary>Everything a reset has to remove, in one place so none is forgotten.</summary>
    private static IEnumerable<string> Targets(string vault)
    {
        yield return vault;
        yield return vault + ".tmp";

        foreach (var database in AppPaths.DatabaseFiles)
        {
            yield return database;
        }
    }

    /// <summary>
    /// Argon2id, on a background thread - it is meant to take a noticeable fraction of a
    /// second, which is exactly how long the UI thread must not be blocked for.
    /// </summary>
    private static Task<byte[]> DeriveKeyAsync(
        string password,
        byte[] salt,
        int memoryKiB,
        int iterations,
        int parallelism,
        CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                var passwordBytes = Encoding.UTF8.GetBytes(password);

                try
                {
                    using var argon2 = new Argon2id(passwordBytes)
                    {
                        Salt = salt,
                        MemorySize = memoryKiB,
                        Iterations = iterations,
                        DegreeOfParallelism = parallelism,
                    };

                    return argon2.GetBytes(KeyBytes);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(passwordBytes);
                }
            },
            cancellationToken);

    /// <summary>
    /// Writes through a temporary file and renames over the original, so a crash or a
    /// power cut cannot leave behind a half-written vault that nothing can ever open.
    /// </summary>
    private async Task WriteAsync(VaultDescriptor descriptor, CancellationToken cancellationToken)
    {
        var path = _vaultPath.Value;
        var temporary = path + ".tmp";

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var stream = File.Create(temporary))
        {
            await JsonSerializer
                .SerializeAsync(stream, descriptor, VaultJsonContext.Default.VaultDescriptor, cancellationToken)
                .ConfigureAwait(false);

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// The packaged app's local folder, with a fallback for running without package
    /// identity so the app is still debuggable unpackaged.
    /// </summary>
    private static string ResolveVaultPath() => Path.Combine(AppPaths.DataFolder, FileName);
}
