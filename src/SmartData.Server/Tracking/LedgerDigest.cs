namespace SmartData.Server.Tracking;

/// <summary>
/// Snapshot of a ledger chain's head at a moment in time. The payload that
/// <c>sp_ledger_digest</c> emits and that external anchor stores record.
/// Feeding a sequence of these back into <c>ctx.Verify&lt;T&gt;(anchors)</c>
/// provides tamper-evidence beyond what the chain alone can detect
/// (cascade rewrites, tail truncation, rollback). See spec § Digests &amp;
/// Anchoring.
/// </summary>
public sealed class LedgerDigest
{
    public string TableName { get; set; } = "";

    /// <summary>The row whose <see cref="LatestRowHash"/> is captured.</summary>
    public long LatestLedgerId { get; set; }

    public byte[] LatestRowHash { get; set; } = [];

    /// <summary>
    /// Timestamp of the row being anchored — the paired history row's
    /// <c>ChangedOn</c> for entity mutations, or the synthetic marker's
    /// <c>DetectedAt</c>/<c>PrunedAt</c> for <c>'S'</c>/<c>'P'</c> rows.
    /// </summary>
    public DateTime ChangedOn { get; set; }

    /// <summary>
    /// Count of rows at capture time. Informational — not authoritative and
    /// not part of the verification contract, since pruning changes the count
    /// without invalidating the chain.
    /// </summary>
    public long EntryCount { get; set; }
}
