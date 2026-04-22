namespace SmartData.Server.Tracking;

/// <summary>
/// Resolved tracking mode for an entity — the outcome of combining the declared
/// attribute with the observed table state. See <c>docs/SmartData.Server.Tracking.md</c>
/// § Lifecycle → Startup resolution.
/// </summary>
public enum TrackingMode
{
    /// <summary>No tracking. Mutations bypass history/ledger entirely.</summary>
    None = 0,

    /// <summary>Mirror mutations to <c>{Table}_History</c>. No integrity chain.</summary>
    Tracked = 1,

    /// <summary>
    /// Mirror to <c>{Table}_History</c> and append a hash-chained row to
    /// <c>{Table}_Ledger</c>. Implies <see cref="Tracked"/>.
    /// </summary>
    Ledger = 2,
}
