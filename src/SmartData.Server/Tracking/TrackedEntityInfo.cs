using System.Reflection;
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

namespace SmartData.Server.Tracking;

/// <summary>
/// Per-entity reflection snapshot for tracking. Computed once per <typeparamref name="T"/>
/// at first use; the result never changes during a process lifetime.
/// </summary>
internal static class TrackedEntityInfo<T> where T : class, new()
{
    private static readonly Lazy<Snapshot> _snapshot = new(Compute, isThreadSafe: true);

    public static TrackingMode DeclaredMode => _snapshot.Value.Mode;
    public static string HistoryTableName => _snapshot.Value.HistoryTableName;
    public static string LedgerTableName => _snapshot.Value.LedgerTableName;
    public static IReadOnlyList<PropertyInfo> MirroredProperties => _snapshot.Value.MirroredProperties;
    public static IReadOnlyList<string> PrimaryKeyColumnNames => _snapshot.Value.PrimaryKeyColumnNames;

    private static Snapshot Compute()
    {
        var t = typeof(T);
        var mode = TrackingMode.None;
        if (t.GetCustomAttribute<TrackedAttribute>(inherit: false) is not null)
            mode = TrackingMode.Tracked;
        // [Ledger] implies [Tracked]. Silently enforced during attribute scan.
        if (t.GetCustomAttribute<LedgerAttribute>(inherit: false) is not null)
            mode = TrackingMode.Ledger;

        var tableName = EntityMapping<T>.GetTableName();
        var historyTableName = $"{tableName}_History";
        var ledgerTableName = $"{tableName}_Ledger";

        var mapping = EntityMapping<T>.GetMappingSchema();
        var descriptor = mapping.GetEntityDescriptor(t);

        var pkNames = descriptor.Columns
            .Where(c => c.IsPrimaryKey)
            .Select(c => c.ColumnName)
            .ToList();

        // Mirrored set: every mapped column on T except those tagged [NotTracked].
        var mirrored = new List<PropertyInfo>();
        foreach (var col in descriptor.Columns)
        {
            if (col.MemberInfo is not PropertyInfo p) continue;
            if (p.GetCustomAttribute<NotTrackedAttribute>(inherit: false) is not null) continue;
            mirrored.Add(p);
        }

        return new Snapshot(mode, historyTableName, ledgerTableName, mirrored, pkNames);
    }

    private sealed record Snapshot(
        TrackingMode Mode,
        string HistoryTableName,
        string LedgerTableName,
        IReadOnlyList<PropertyInfo> MirroredProperties,
        IReadOnlyList<string> PrimaryKeyColumnNames);
}
