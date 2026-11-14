using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EasyNet.Services;

/// <summary>The vault's front door: whether it exists, and whether it opens.</summary>
public interface IVaultService
{
    /// <summary>True once a password has been created for this machine's vault.</summary>
    bool IsCreated { get; }

    /// <summary>True while the vault is unlocked.</summary>
    bool IsUnlocked { get; }

    /// <summary>Creates the vault for a first password. The vault is unlocked afterwards.</summary>
    Task CreateAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>Tries the password. True and unlocked on success, false on a wrong password.</summary>
    Task<bool> TryUnlockAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>Locks the vault. The password is asked for again to reopen it.</summary>
    void Lock();
}

/// <summary>
/// The vault's front door, on disk.
/// </summary>
/// <remarks>
/// <para>
/// What is stored is a verifier, not the password and not any data: a fixed
/// 32-byte token, sealed with AES-GCM under a key derived from the password
/// with PBKDF2-HMAC-SHA256 over 210,000 iterations and a random 16-byte
/// salt. A wrong password fails the cipher's own authentication - the
/// comparison never even happens - and the wrong-password path costs the
/// same key derivation as the right one, so timing says nothing about
/// correctness.
/// </para>
/// <para>
/// When the vault grows real data, this same derived key is what encrypts it;
/// the verifier is the door, not the room. The file is written whole and
/// moved into place, so a power cut mid-write can never leave half a vault.
///</para>
///</remarks>
public sealed class VaultService : IVaultService
{
    private const int Pbkdf2Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TokenBytes = 32;
    private const int TagBytes = 16;

    // What gets sealed. Any fixed 32 bytes would do; these are the hash of a
    // sentence, so they are not a value anyone would plausibly store anywhere
    // else, and they are computed once per process.
    private static readonly byte[] VerifierToken = SHA256.HashData(
        "EasyNet vault verifier v1"u8);

    private readonly string _vaultFile;
    private readonly object _gate = new();

    /// <inheritdoc/>
    public bool IsCreated { get; private set; }

    /// <inheritdoc/>
    public bool IsUnlocked { get; private set; }

    public VaultService(AppPaths paths)
    {
        _vaultFile = paths.VaultFile;
        IsCreated = File.Exists(_vaultFile);
    }

    /// <inheritdoc/>
    public async Task CreateAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceBytes);

        byte[] key = await DeriveKeyAsync(password, salt, cancellationToken).ConfigureAwait(false);
        byte[] seal = SealedToken(key, nonce);

        VaultRecord record = new(
            Version: 1,
            Salt: Convert.ToBase64String(salt),
            Nonce: Convert.ToBase64String(nonce),
            Seal: Convert.ToBase64String(seal));

        await WriteAtomicallyAsync(record, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            IsCreated = true;
            IsUnlocked = true;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryUnlockAsync(string password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(password);

        VaultRecord? record;
        try
        {
            await using Stream stream = File.OpenRead(_vaultFile);
            record = await JsonSerializer.DeserializeAsync<VaultRecord>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (
            error is IOException or UnauthorizedAccessException or JsonException)
        {
            // A vault that cannot be read is not a vault that opens.
            return false;
        }

        if (record is not { Version: 1 })
        {
            return false;
        }

        byte[] salt;
        byte[] nonce;
        byte[] seal;

        try
        {
            salt = Convert.FromBase64String(record.Salt);
            nonce = Convert.FromBase64String(record.Nonce);
            seal = Convert.FromBase64String(record.Seal);
        }
        catch (FormatException)
        {
            // A vault whose file is not what it says it is does not open.
            return false;
        }

        if (seal.Length != TokenBytes + TagBytes)
        {
            return false;
        }

        byte[] key = await DeriveKeyAsync(password, salt, cancellationToken).ConfigureAwait(false);

        // The cipher authenticates before anything is compared, and a wrong
        // key fails here - which is the entire wrong-password story.
        byte[] token = new byte[TokenBytes];
        try
        {
            using var cipher = new AesGcm(key, TagBytes);

            // The seal is token-then-tag: the ciphertext is only its first
            // TokenBytes. Handing the whole seal in as the ciphertext asked
            // for a 48-byte plaintext into a 32-byte buffer, and the cipher
            // answered with an argument error no catch clause here was for -
            // which is how every unlock attempt once died silently and took
            // the button's enabled state down with it.
            cipher.Decrypt(nonce, seal.AsSpan(0, TokenBytes), seal.AsSpan(TokenBytes), token);
        }
        catch (CryptographicException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(token, VerifierToken))
        {
            return false;
        }

        lock (_gate)
        {
            IsUnlocked = true;
        }

        return true;
    }

    /// <inheritdoc/>
    public void Lock()
    {
        lock (_gate)
        {
            IsUnlocked = false;
        }
    }

    /// <summary>Derives the vault key off the UI thread - 210,000 rounds are not instant.</summary>
    private static Task<byte[]> DeriveKeyAsync(string password, byte[] salt, CancellationToken cancellationToken) =>
        Task.Run(
            () => Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, TokenBytes),
            cancellationToken);

    private static byte[] SealedToken(byte[] key, byte[] nonce)
    {
        byte[] seal = new byte[VerifierToken.Length + TagBytes];
        using (var cipher = new AesGcm(key, TagBytes))
        {
            cipher.Encrypt(nonce, VerifierToken, seal.AsSpan(0, VerifierToken.Length),
                           seal.AsSpan(VerifierToken.Length));
        }

        return seal;
    }

    /// <summary>Writes the record whole, then moves it into place.</summary>
    private async Task WriteAtomicallyAsync(VaultRecord record, CancellationToken cancellationToken)
    {
        string temporary = _vaultFile + ".tmp";

        await using (Stream stream = File.Create(temporary))
        {
            await JsonSerializer.SerializeAsync(stream, record, cancellationToken:  cancellationToken)
                .ConfigureAwait(false);
        }

        File.Move(temporary, _vaultFile, overwrite: true);
    }

}

/// <summary>
/// The vault file's shape. Public because the serialiser reflects over it;
/// the field names are short because they are on disk, not in code.
/// </summary>
public sealed record VaultRecord(int Version, string Salt, string Nonce, string Seal);
