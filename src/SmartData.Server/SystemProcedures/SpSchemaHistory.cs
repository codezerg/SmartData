using System.Text.Json;
using LinqToDB;
using LinqToDB.Data;
using SmartData.Core.BinarySerialization;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Tracking;

namespace SmartData.Server.SystemProcedures;

/// <summary>
/// Returns the schema-marker timeline for a tracked entity. Dispatches on
/// table existence — if <c>{table}_Ledger</c> is present the timeline is
/// read from chained <c>'S'</c> markers (integrity-protected); otherwise
/// falls back to <c>SysTrackedColumns</c> (sidecar, no integrity claim).
///
/// <para>Parameter <c>table</c>: the source entity's table name.</para>
/// </summary>
internal class SpSchemaHistory : SystemStoredProcedure<SchemaHistoryResult>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";

    public override SchemaHistoryResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.LedgerRead, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrWhiteSpace(Table))
            RaiseError("Parameter 'table' is required.");

        var ledgerTable = $"{Table}_Ledger";
        var historyTable = $"{Table}_History";

        // Type-agnostic — can't use ctx.SchemaMarkers<T>(). Open a raw connection
        // to the named DB and branch on table existence.
        using var conn = provider.OpenConnection(Database);

        if (provider.Schema.GetTableSchema(Database, ledgerTable).Exists)
            return ReadFromLedger(conn, ledgerTable);

        if (provider.Schema.GetTableSchema(Database, historyTable).Exists)
            return ReadFromSidecar(conn, Table);

        RaiseError($"Table '{Table}' is neither ledgered nor tracked in '{Database}'.");
        return null!;
    }

    private static SchemaHistoryResult ReadFromLedger(DataConnection conn, string ledgerTable)
    {
        var rows = conn.Query<LedgerRow>(
            $"SELECT LedgerId, HistoryId, FormatVersion, CanonicalBytes, PrevHash, RowHash " +
            $"FROM [{ledgerTable}] WHERE HistoryId IS NULL ORDER BY LedgerId").ToList();

        var entries = new List<SchemaHistoryEntry>();
        bool first = true;
        foreach (var row in rows)
        {
            try
            {
                var payload = BinarySerializer.Deserialize<LedgerPayload<object>>(row.CanonicalBytes);
                if (payload?.Synthetic?.Schema is not { } marker) continue;
                entries.Add(new SchemaHistoryEntry
                {
                    DetectedAt = marker.DetectedAt,
                    DetectedBy = marker.DetectedBy,
                    CapturedHash = marker.CapturedHash,
                    Columns = marker.Columns,
                    Added = marker.Added,
                    Removed = marker.Removed,
                    IsBaseline = first,
                });
                first = false;
            }
            catch { /* unreadable — skip */ }
        }
        return new SchemaHistoryResult { Source = "ledger", Entries = entries };
    }

    private static SchemaHistoryResult ReadFromSidecar(DataConnection conn, string tableName)
    {
        var row = conn.GetTable<SysTrackedColumns>()
            .FirstOrDefault(r => r.TableName == tableName);
        if (row is null) return new SchemaHistoryResult { Source = "sidecar", Entries = [] };

        var cols = TryParseColumns(row.CapturedColumnsJson);
        return new SchemaHistoryResult
        {
            Source = "sidecar",
            Entries = new List<SchemaHistoryEntry>
            {
                new()
                {
                    DetectedAt = row.StampedOn,
                    DetectedBy = row.StampedBy,
                    CapturedHash = row.CapturedHash,
                    Columns = cols,
                    Added = [],
                    Removed = [],
                    IsBaseline = true,
                }
            }
        };
    }

    private static CapturedColumn[] TryParseColumns(string json)
    {
        try { return JsonSerializer.Deserialize<CapturedColumn[]>(json) ?? []; }
        catch { return []; }
    }
}

// (LedgerRow is declared in LedgerVerifier.cs as internal.)
