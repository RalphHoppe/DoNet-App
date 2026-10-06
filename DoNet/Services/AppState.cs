using System;
using System.IO;
using DoNet.Contracts;
using Windows.Storage;

namespace DoNet.Services;

/// <inheritdoc cref="IAppState" />
/// <remarks>
/// Deliberately the thinnest thing that answers the question. "Has a password been set"
/// is decided by whether the vault descriptor exists on disk - the file that will hold
/// the KDF parameters, the salt and the wrapped key once that work lands. Until then no
/// such file is ever written, so a fresh install always routes to onboarding.
/// <para>
/// When the vault is built, this is the one place that needs revisiting.
/// </para>
/// </remarks>
public sealed class AppState : IAppState
{
    private const string VaultFileName = "vault.json";

    public bool IsPasswordConfigured
    {
        get
        {
            try
            {
                return File.Exists(Path.Combine(ApplicationData.Current.LocalFolder.Path, VaultFileName));
            }
            catch (Exception)
            {
                // ApplicationData.Current throws when the app is running without package
                // identity. Routing to onboarding is the safe answer: the worst case is
                // that the user is asked to set a password they have already set, which
                // is recoverable, whereas a lock screen with no vault behind it is not.
                return false;
            }
        }
    }
}
