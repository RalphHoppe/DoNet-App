using System.Threading;
using System.Threading.Tasks;

namespace DoNet.Contracts;

/// <summary>
/// Owns the master password: creating it, storing it in a form that cannot be read back,
/// and deciding whether a password offered later is the right one.
/// </summary>
public interface IVaultService
{
    /// <summary>
    /// True once a master password has been set on this machine. Decides whether the
    /// splash hands over to onboarding or to the lock screen.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>True while the data key is held, i.e. between unlocking and locking.</summary>
    bool IsUnlocked { get; }

    /// <summary>
    /// The key the encrypted database is opened with. Valid only while
    /// <see cref="IsUnlocked"/>; throws otherwise rather than returning something
    /// unusable, because a silent empty key would create a second, unencrypted database.
    /// </summary>
    string DatabaseKey { get; }

    /// <summary>Drops the data key, closing the database to further reads.</summary>
    void Lock();

    /// <summary>
    /// Creates the vault for <paramref name="password"/>.
    /// </summary>
    /// <remarks>
    /// Expensive by design - the key derivation is deliberately slow - so this is async
    /// and must not be awaited on a tight UI path without a busy indicator.
    /// </remarks>
    Task CreateAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks <paramref name="password"/> against the stored vault.
    /// </summary>
    /// <returns>True if it was correct.</returns>
    Task<bool> TryUnlockAsync(string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Destroys the vault, returning the installation to its first-run state.
    /// </summary>
    /// <remarks>
    /// Irreversible, and meant to be. Without the password the wrapped data key can
    /// never be recovered, so everything it protects is already unreadable - this only
    /// clears the way for a new password.
    /// </remarks>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
