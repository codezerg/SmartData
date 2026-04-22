namespace SmartData.Server.Tracking;

/// <summary>
/// Return shape for <c>sp_schema_history</c>. One entry per recorded snapshot:
/// a genesis or drift <c>'S'</c> marker for <c>[Ledger]</c> tables, or the
/// single sidecar baseline row for <c>[Tracked]</c>-only tables.
/// </summary>
public sealed class SchemaHistoryResult
{
    /// <summary>
    /// Where the timeline came from. <c>"ledger"</c> = chained and
    /// integrity-protected; <c>"sidecar"</c> = mutable, operator-visible only
    /// (no integrity claim — see spec § Schema Drift → <c>[Tracked]</c>-only).
    /// </summary>
    public string Source { get; set; } = "";

    public List<SchemaHistoryEntry> Entries { get; set; } = new();
}

public sealed class SchemaHistoryEntry
{
    public DateTime DetectedAt { get; set; }
    public string DetectedBy { get; set; } = "";
    public byte[] CapturedHash { get; set; } = [];
    public CapturedColumn[] Columns { get; set; } = [];
    public string[] Added { get; set; } = [];
    public string[] Removed { get; set; } = [];

    /// <summary>True for the first <c>'S'</c> marker in a chain / the sidecar baseline.</summary>
    public bool IsBaseline { get; set; }
}
