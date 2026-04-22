namespace SmartData.Server.Attributes;

/// <summary>
/// Marks an entity class for history tracking. Every INSERT/UPDATE/DELETE through
/// the SmartData entity write path (<c>ctx.Insert</c>, <c>ctx.Update</c>, <c>ctx.Delete</c>)
/// produces a mirrored row in <c>{Table}_History</c> with audit metadata
/// (<c>Operation</c>, <c>ChangedOn</c>, <c>ChangedBy</c>).
///
/// <para>
/// The attribute is <b>sticky</b>: once applied and the history table exists,
/// removing the attribute does NOT stop tracking — the framework treats table
/// existence as the persistent record of intent and continues to append history
/// rows (with a startup warning). Use <c>sp_tracking_drop(table, confirm)</c> to
/// explicitly stop.
/// </para>
///
/// <para>
/// See <c>docs/SmartData.Server.Tracking.md</c> for full semantics, including
/// schema evolution, captured-set policy, and interaction with <c>[Ledger]</c>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TrackedAttribute : Attribute
{
}
