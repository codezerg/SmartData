namespace SmartData.Server.Attributes;

/// <summary>
/// Marks an entity class for tamper-evident ledger tracking. Implies
/// <see cref="TrackedAttribute"/>: every mutation writes to both
/// <c>{Table}_History</c> (queryable) and <c>{Table}_Ledger</c> (hash-chained,
/// integrity-protected).
///
/// <para>
/// Like <see cref="TrackedAttribute"/>, <see cref="LedgerAttribute"/> is sticky:
/// once the ledger table exists, removing the attribute does NOT stop ledgering.
/// Use <c>sp_ledger_drop(table, confirm)</c> to downgrade to <see cref="TrackedAttribute"/>
/// or <c>sp_tracking_drop</c> to remove all tracking.
/// </para>
///
/// <para>
/// See <c>docs/SmartData.Server.Tracking.md</c> for the full concurrency model,
/// schema-drift semantics, pruning, and digest/anchoring guidance.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class LedgerAttribute : Attribute
{
}
