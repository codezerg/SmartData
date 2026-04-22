namespace SmartData.Server.Tracking;

/// <summary>
/// Result of a ledger integrity check — see spec § Read Path → <c>ctx.Verify&lt;T&gt;</c>.
/// Split into two sections: internal chain consistency (always populated) and
/// anchor verification (populated only when anchors were supplied).
/// </summary>
public sealed class VerificationResult
{
    /// <summary>
    /// True iff <see cref="InternalConsistencyValid"/> holds AND every supplied
    /// anchor matched (or was explicitly superseded by a covering prune event).
    /// </summary>
    public bool Valid { get; init; }

    // ---- Internal chain consistency -----------------------------------------

    /// <summary>Forward-walk succeeded: every row self-hashes and forward-links correctly.</summary>
    public bool InternalConsistencyValid { get; init; }

    /// <summary>First <c>LedgerId</c> whose self-hash or forward link failed. Null on success.</summary>
    public long? FirstBadLedgerId { get; init; }

    /// <summary>Nature of the first internal failure. Null on success.</summary>
    public VerificationFailure? FailureKind { get; init; }

    /// <summary>Number of rows walked before stopping (failure or success).</summary>
    public long EntriesVerified { get; init; }

    // ---- Anchor verification ------------------------------------------------

    public int AnchorsChecked { get; init; }
    public int AnchorsMatched { get; init; }

    /// <summary>
    /// Anchor references a row that falls inside a pruned range (its paired
    /// prune marker is still present). Historical rather than tampered.
    /// </summary>
    public int AnchorsSuperseded { get; init; }

    public IReadOnlyList<AnchorFailure> AnchorFailures { get; init; } = [];

    /// <summary>Newest anchor that matched — the last known good state.</summary>
    public DateTime? LastKnownGood { get; init; }

    /// <summary>Oldest anchor that failed — lower bound of the tamper window.</summary>
    public DateTime? EarliestTamper { get; init; }
}

public enum VerificationFailure
{
    /// <summary>Stored <c>RowHash</c> does not equal the recomputed hash.</summary>
    RowHashMismatch,

    /// <summary>Forward link broken: <c>next.PrevHash != this.RowHash</c>.</summary>
    PrevHashBroken,
}

/// <summary>
/// Captured failure information for a single anchor that did not match the
/// current chain.
/// </summary>
public sealed class AnchorFailure
{
    public DateTime AnchorTime { get; init; }
    public long AnchorLedgerId { get; init; }
    public byte[] ExpectedHash { get; init; } = [];
    public byte[]? ActualHash { get; init; }
    public AnchorFailureKind Kind { get; init; }
}

public enum AnchorFailureKind
{
    /// <summary>
    /// Row exists at the anchored <c>LedgerId</c>, but its <c>RowHash</c>
    /// differs from the anchor's. Evidence of a cascade rewrite.
    /// </summary>
    RowHashMismatch,

    /// <summary>
    /// Row is gone and no covering prune event accounts for it. Evidence of
    /// truncation or unrecorded deletion.
    /// </summary>
    RowMissing,

    /// <summary>
    /// Current <c>MAX(LedgerId)</c> is below the anchored <c>LedgerId</c>.
    /// Evidence of a rollback.
    /// </summary>
    ChainRegression,
}
