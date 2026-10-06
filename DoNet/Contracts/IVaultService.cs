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
}
