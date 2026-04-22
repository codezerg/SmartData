using System.Reflection;
using Microsoft.Extensions.Logging;
using SmartData.Server.Entities;
using SmartData.Server.Providers;

namespace SmartData.Server.Tracking;

/// <summary>
/// Provisions <c>{Table}_History</c> alongside a tracked entity's source table.
/// One dedupe guard per (db, entity) pair — runs at most once per process.
///
/// <para>
/// Called from <see cref="SchemaManager{T}.EnsureSchema"/> immediately after
/// the source table is ensured, so history provisioning shares the same
/// connection, locks, and IndexOptions. See
/// <c>docs/SmartData.Server.Tracking.md</c> § Storage → <c>{Table}_History</c>.
/// </para>
/// </summary>
internal static class TrackingSchemaManager<T> where T : class, new()
{
    private static readonly object _lock = new();
    private static readonly HashSet<string> _ensured = new(StringComparer.OrdinalIgnoreCase);

    public static void EnsureHistorySchema(string dbName, IDatabaseProvider provider, IndexOptions indexOptions)
    {
        var mode = TrackedEntityInfo<T>.DeclaredMode;

        // Sticky resolution — warn when declared attribute and observed tables
        // disagree. Spec § Lifecycle → Startup resolution.
        WarnOnDrift(dbName, provider, mode);

        if (mode == TrackingMode.None) return;

        // Provision the per-DB sidecar table for [Tracked]-only drift detection.
        // SchemaManager<T> dedupes per (db, entity) so this costs nothing after
        // the first tracked entity in a given DB.
        SchemaManager<SysTrackedColumns>.EnsureSchema(dbName, provider, indexOptions);

        EnsureHistoryTable(dbName, provider, indexOptions);
        if (mode == TrackingMode.Ledger)
            EnsureLedgerTable(dbName, provider, indexOptions);
    }

    private static readonly HashSet<string> _driftWarned = new(StringComparer.OrdinalIgnoreCase);

    private static void WarnOnDrift(string dbName, IDatabaseProvider provider, TrackingMode declared)
    {
        var sourceTable = EntityMapping<T>.GetTableName();
        var warnKey = $"{dbName}::{sourceTable}";

        lock (_driftWarned)
        {
            if (_driftWarned.Contains(warnKey)) return;
            _driftWarned.Add(warnKey);
        }

        var historyExists = provider.Schema.GetTableSchema(dbName, $"{sourceTable}_History").Exists;
        var ledgerExists = provider.Schema.GetTableSchema(dbName, $"{sourceTable}_Ledger").Exists;

        // [Tracked] attribute, but _Ledger also exists → the entity was
        // previously ledgered. Table existence wins — continue ledgering, warn.
        if (declared == TrackingMode.Tracked && historyExists && ledgerExists)
        {
            TrackingLog.Logger.LogWarning(
                "Entity '{Table}' declares [Tracked] but ledger table exists in database '{Db}'. " +
                "Continuing in Ledger mode. To stop, call sp_ledger_drop(database='{Db}', table='{Table}', confirm='{Table}').",
                sourceTable, dbName, dbName, sourceTable, sourceTable);
            return;
        }

        // No attribute, but tables exist → attribute was removed at some
        // point. Preserve tracking (table existence is the persistent record
        // of intent), warn.
        if (declared == TrackingMode.None && ledgerExists)
        {
            TrackingLog.Logger.LogWarning(
                "Entity '{Table}' has no [Tracked]/[Ledger] attribute, but ledger table exists in database '{Db}'. " +
                "Continuing in Ledger mode. To stop, call sp_tracking_drop(database='{Db}', table='{Table}', confirm='{Table}').",
                sourceTable, dbName, dbName, sourceTable, sourceTable);
            return;
        }
        if (declared == TrackingMode.None && historyExists)
        {
            TrackingLog.Logger.LogWarning(
                "Entity '{Table}' has no [Tracked]/[Ledger] attribute, but history table exists in database '{Db}'. " +
                "Continuing in Tracked mode. To stop, call sp_tracking_drop(database='{Db}', table='{Table}', confirm='{Table}').",
                sourceTable, dbName, dbName, sourceTable, sourceTable);
            return;
        }
    }

    private static void EnsureHistoryTable(string dbName, IDatabaseProvider provider, IndexOptions indexOptions)
    {
        var historyTable = TrackedEntityInfo<T>.HistoryTableName;
        var key = $"{dbName}::{historyTable}";
        if (_ensured.Contains(key)) return;

        lock (_lock)
        {
            if (_ensured.Contains(key)) return;

            var schemaOps = provider.SchemaOperations;
            var snapshot = provider.Schema.GetTableSchema(dbName, historyTable);
            var columns = BuildColumnDefinitions();

            if (!snapshot.Exists)
            {
                schemaOps.CreateTable(dbName, historyTable, columns);
                EnsureHistoryIndexes(dbName, historyTable, [], provider, indexOptions);
                _ensured.Add(key);
                return;
            }

            // Additive migration: add any columns that exist on the entity but
            // not yet on the history table. Type-changes and drops are not
            // auto-migrated in v1 — see spec § Schema Evolution.
            foreach (var col in columns)
            {
                var match = snapshot.Columns.FirstOrDefault(c =>
                    string.Equals(c.Name, col.Name, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    var sqlType = schemaOps.MapType(col.Type, col.Length);
                    schemaOps.AddColumn(dbName, historyTable, col.Name, sqlType, col.Nullable);
                }
            }

            EnsureHistoryIndexes(dbName, historyTable, snapshot.Indexes, provider, indexOptions);
            _ensured.Add(key);
        }
    }

    private static void EnsureLedgerTable(string dbName, IDatabaseProvider provider, IndexOptions indexOptions)
    {
        var ledgerTable = TrackedEntityInfo<T>.LedgerTableName;
        var key = $"{dbName}::{ledgerTable}";
        if (_ensured.Contains(key)) return;

        lock (_lock)
        {
            if (_ensured.Contains(key)) return;

            var schemaOps = provider.SchemaOperations;
            var snapshot = provider.Schema.GetTableSchema(dbName, ledgerTable);

            // Fixed, entity-independent shape. PrevHash and RowHash are sized
            // to the SHA-256 digest length (32 bytes) so SQL Server can index
            // them — VARBINARY(MAX) isn't allowed as a key column.
            var cols = new List<Providers.ColumnDefinition>
            {
                new("LedgerId",       "long",    Nullable: false, PrimaryKey: true,  Identity: true),
                new("HistoryId",      "long",    Nullable: true,  PrimaryKey: false),
                new("FormatVersion",  "int",     Nullable: false, PrimaryKey: false),
                new("CanonicalBytes", "byte[]",  Nullable: false, PrimaryKey: false),
                new("PrevHash",       "byte[]",  Nullable: false, PrimaryKey: false, Length: 32),
                new("RowHash",        "byte[]",  Nullable: false, PrimaryKey: false, Length: 32),
            };

            if (!snapshot.Exists)
            {
                schemaOps.CreateTable(dbName, ledgerTable, cols);
                EnsureLedgerIndexes(dbName, ledgerTable, [], provider, indexOptions);
            }
            else
            {
                // Ledger shape is fixed — we never alter columns on it. The only
                // migration concern is missing indexes (e.g., upgrade from an older
                // build), which EnsureLedgerIndexes handles idempotently.
                EnsureLedgerIndexes(dbName, ledgerTable, snapshot.Indexes, provider, indexOptions);
            }

            _ensured.Add(key);
        }
    }

    private static List<ColumnDefinition> BuildColumnDefinitions()
    {
        var cols = new List<ColumnDefinition>
        {
            new("HistoryId", "long",     Nullable: false, PrimaryKey: true, Identity: true),
            new("Operation", "string",   Nullable: false, PrimaryKey: false, Length: 1),
            new("ChangedOn", "datetime", Nullable: false, PrimaryKey: false),
            new("ChangedBy", "string",   Nullable: false, PrimaryKey: false, Length: 200),
        };

        foreach (var p in TrackedEntityInfo<T>.MirroredProperties)
            cols.Add(BuildMirroredColumn(p));

        return cols;
    }

    private static ColumnDefinition BuildMirroredColumn(PropertyInfo p)
    {
        // History columns never declare primary-key/identity — that belongs to HistoryId.
        // All mirrored columns are nullable: historical rows pre-dating an entity-column
        // change wouldn't have values, and even NOT-NULL source columns can carry NULL
        // in history after a column add.
        var logicalType = MapClrToLogicalType(p.PropertyType);
        var length = ResolveLength(p);
        return new ColumnDefinition(
            Name: p.Name,
            Type: logicalType,
            Nullable: true,
            PrimaryKey: false,
            Identity: false,
            Length: length);
    }

    private static string MapClrToLogicalType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t == typeof(string)) return "string";
        if (t == typeof(int)) return "int";
        if (t == typeof(long)) return "long";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(DateTime)) return "datetime";
        if (t == typeof(Guid)) return "guid";
        if (t == typeof(byte[])) return "byte[]";
        if (t == typeof(decimal)) return "decimal";
        if (t == typeof(double) || t == typeof(float)) return "double";
        if (t.IsEnum) return "int";
        return "string";
    }

    private static int? ResolveLength(PropertyInfo p)
    {
        var col = p.GetCustomAttribute<LinqToDB.Mapping.ColumnAttribute>();
        if (col?.Length is > 0) return col.Length;
        var max = p.GetCustomAttribute<System.ComponentModel.DataAnnotations.MaxLengthAttribute>();
        if (max?.Length is > 0) return max.Length;
        var strLen = p.GetCustomAttribute<System.ComponentModel.DataAnnotations.StringLengthAttribute>();
        if (strLen?.MaximumLength is > 0) return strLen.MaximumLength;
        return null;
    }

    private static void EnsureHistoryIndexes(
        string dbName, string historyTable,
        IReadOnlyList<ProviderIndexInfo> existing,
        IDatabaseProvider provider, IndexOptions options)
    {
        if (!options.AutoCreate) return;
        var schemaOps = provider.SchemaOperations;

        // Index on source PK column(s) — supports "all history for row 42" queries.
        var pkCols = TrackedEntityInfo<T>.PrimaryKeyColumnNames;
        if (pkCols.Count > 0)
        {
            var pkIxName = $"{options.Prefix}ix_{historyTable}_pk".ToLowerInvariant();
            if (!existing.Any(i => string.Equals(i.Name, pkIxName, StringComparison.OrdinalIgnoreCase)))
            {
                var cols = string.Join(", ", pkCols.Select(c => $"[{c}]"));
                schemaOps.CreateIndex(dbName, historyTable, pkIxName, cols, unique: false);
            }
        }

        // Index on ChangedOn — supports time-range queries.
        var tsIxName = $"{options.Prefix}ix_{historyTable}_changed_on".ToLowerInvariant();
        if (!existing.Any(i => string.Equals(i.Name, tsIxName, StringComparison.OrdinalIgnoreCase)))
            schemaOps.CreateIndex(dbName, historyTable, tsIxName, "[ChangedOn]", unique: false);
    }

    private static void EnsureLedgerIndexes(
        string dbName, string ledgerTable,
        IReadOnlyList<ProviderIndexInfo> existing,
        IDatabaseProvider provider, IndexOptions options)
    {
        if (!options.AutoCreate) return;
        var schemaOps = provider.SchemaOperations;

        // UNIQUE(PrevHash) — the chain arbitration mechanism. Load-bearing; see
        // spec § Concurrency. Without this the lock-free design fails open.
        var prevIx = $"{options.Prefix}ux_{ledgerTable}_prevhash".ToLowerInvariant();
        if (!existing.Any(i => string.Equals(i.Name, prevIx, StringComparison.OrdinalIgnoreCase)))
            schemaOps.CreateIndex(dbName, ledgerTable, prevIx, "[PrevHash]", unique: true);

        // UNIQUE(HistoryId) WHERE HistoryId IS NOT NULL — pairs the ledger row
        // with its history row. Multiple NULLs are allowed so synthetic rows
        // (schema / prune markers) don't collide.
        var hidIx = $"{options.Prefix}ux_{ledgerTable}_historyid".ToLowerInvariant();
        if (!existing.Any(i => string.Equals(i.Name, hidIx, StringComparison.OrdinalIgnoreCase)))
            schemaOps.CreateIndex(dbName, ledgerTable, hidIx, "[HistoryId]",
                unique: true, whereClause: "[HistoryId] IS NOT NULL");
    }

    internal static void ResetForTesting()
    {
        lock (_lock) _ensured.Clear();
    }
}
