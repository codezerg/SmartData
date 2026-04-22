using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;

namespace SmartData.Server.Entities;

/// <summary>
/// Operator-visible drift detection record for <c>[Tracked]</c>-only entities
/// — those without a hash chain to anchor drift against. One row per entity
/// table. Carries <b>no</b> integrity claim: this table is mutable by design
/// and can be rewritten without consequence. Use the ledger chain's own <c>'S'</c>
/// markers for audit-grade drift history. See spec § Schema Drift →
/// <c>[Tracked]</c>-only tables.
/// </summary>
[Table("_sys_tracked_columns")]
internal class SysTrackedColumns
{
    /// <summary>Source entity table name, e.g. <c>"Customer"</c>.</summary>
    [PrimaryKey]
    [Column, MaxLength(200)] public string TableName { get; set; } = "";

    /// <summary>
    /// JSON array of <c>{Name, ClrType}</c> objects, sorted by Name. Human-
    /// readable on purpose — this sidecar is meant to be inspected.
    /// </summary>
    [Column] public string CapturedColumnsJson { get; set; } = "";

    /// <summary>SHA-256 of the canonical captured-set encoding. 32 bytes.</summary>
    [Column] public byte[] CapturedHash { get; set; } = [];

    /// <summary>UTC timestamp of the last baseline update.</summary>
    [Column] public DateTime StampedOn { get; set; }

    /// <summary>User who observed the drift — resolved from <c>ITrackingUserProvider</c>.</summary>
    [Column, MaxLength(200)] public string StampedBy { get; set; } = "";
}
