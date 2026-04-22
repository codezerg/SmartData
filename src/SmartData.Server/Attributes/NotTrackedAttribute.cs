namespace SmartData.Server.Attributes;

/// <summary>
/// Excludes a property from both <c>{Table}_History</c> mirrored columns and
/// <c>{Table}_Ledger.CanonicalBytes</c>. Typical use: secrets, PII, or large
/// denormalized blobs whose value isn't worth preserving in the audit trail.
///
/// <para>
/// Adding or removing this attribute mid-life changes the <i>captured set</i>
/// for the entity. On <c>[Ledger]</c> entities, that drift is detected at
/// startup and recorded as a chained schema marker (<c>Operation = 'S'</c>).
/// On <c>[Tracked]</c>-only entities, drift is logged and recorded in the
/// <c>SysTrackedColumns</c> sidecar. See <c>docs/SmartData.Server.Tracking.md</c> §
/// Schema Drift.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class NotTrackedAttribute : Attribute
{
}
