using System.Text.Json.Serialization;

namespace DoNet.Services;

/// <summary>
/// The on-disk shape of <c>vault.json</c>.
/// </summary>
/// <remarks>
/// Everything here is public knowledge. The salt, the nonce and the KDF parameters are
/// not secrets - they only have to be unpredictable or unique, not hidden - and the
/// wrapped key is useless without the password. The password itself is never stored in
/// any form, not even hashed: whether it was right is decided by whether the wrapped key
/// decrypts, which is what the authentication tag is for.
/// </remarks>
internal sealed class VaultDescriptor
{
    /// <summary>Schema version, so a future format change can migrate rather than guess.</summary>
    public int Version { get; set; } = 1;

    public string Kdf { get; set; } = "argon2id";

    /// <summary>Argon2 memory cost, in KiB.</summary>
    public int MemoryKiB { get; set; }

    public int Iterations { get; set; }

    public int Parallelism { get; set; }

    /// <summary>Per-vault KDF salt, base64.</summary>
    public string Salt { get; set; } = string.Empty;

    /// <summary>AES-GCM nonce for the wrapped key, base64.</summary>
    public string Nonce { get; set; } = string.Empty;

    /// <summary>The data key, encrypted under the password-derived key, base64.</summary>
    public string WrappedKey { get; set; } = string.Empty;

    /// <summary>GCM authentication tag, base64. This is what verifies the password.</summary>
    public string Tag { get; set; } = string.Empty;

    public string CreatedUtc { get; set; } = string.Empty;
}

/// <summary>
/// Source-generated serialisation for <see cref="VaultDescriptor"/>.
/// </summary>
/// <remarks>
/// Required rather than optional: the project publishes with PublishTrimmed, and
/// reflection-based System.Text.Json would have its property metadata trimmed away and
/// fail at runtime in Release but not in Debug.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VaultDescriptor))]
internal sealed partial class VaultJsonContext : JsonSerializerContext
{
}
