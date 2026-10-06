using System;
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

    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        // The old key cannot open anything after the vault is gone.
        _databaseKey = null;

        var path = _vaultPath.Value;

        if (Directory.Exists(Path.GetDirectoryName(path)))
        {
            // Deleting a file that is not there is not an error; deleting one in a
            // directory that is not there is, hence the guard above.
            File.Delete(path);
            File.Delete(path + ".tmp");
        }

        // The encrypted database is deleted here too once it exists. It is unreadable
        // without the data key either way, but leaving it behind would waste the space
        // and confuse the next setup.
        return Task.CompletedTask;
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
