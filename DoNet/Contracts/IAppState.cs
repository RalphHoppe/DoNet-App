namespace DoNet.Contracts;

/// <summary>
/// Answers the single question the splash screen has to ask: has this installation been
/// set up yet?
/// </summary>
public interface IAppState
{
    /// <summary>
    /// False on a first run, true on every launch afterwards. Decides whether the splash
    /// hands over to onboarding or to the lock screen.
    /// </summary>
    bool IsPasswordConfigured { get; }
}
