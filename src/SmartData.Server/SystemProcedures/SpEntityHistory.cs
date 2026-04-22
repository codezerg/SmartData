using LinqToDB.Data;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

/// <summary>
/// Returns the history timeline for a single source row, newest first. Backs
/// the admin console's Entity→History tab (spec § Read Path →
/// <c>sp_entity_history</c>).
///
/// <para>
/// Parameters: <c>database</c>, <c>table</c> (source entity name),
/// <c>pk</c> (string primary-key value — composite PKs not supported in v1),
/// <c>limit</c> (default 100), <c>offset</c> (default 0).
/// </para>
/// </summary>
internal class SpEntityHistory : SystemStoredProcedure<SpEntityHistory.Result>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Pk { get; set; } = "";
    public int Limit { get; set; } = 100;
    public int Offset { get; set; } = 0;

    public sealed class Result
    {
        public List<Entry> Items { get; set; } = new();
        public long Total { get; set; }
    }

    public sealed class Entry
    {
        public long HistoryId { get; set; }
        public string Operation { get; set; } = "";
        public DateTime ChangedOn { get; set; }
        public string ChangedBy { get; set; } = "";
        public Dictionary<string, object?> State { get; set; } = new();
    }

    public override Result Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.LedgerRead, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrWhiteSpace(Table) || string.IsNullOrWhiteSpace(Pk))
            RaiseError("'table' and 'pk' are required.");

        var historyTable = $"{Table}_History";
        if (!provider.Schema.GetTableSchema(Database, historyTable).Exists)
            RaiseError($"No history table '{historyTable}' in database '{Database}'.");

        // Resolve the source table's PK column — single-column only in v1.
        var sourceSchema = provider.Schema.GetTableSchema(Database, Table);
        if (!sourceSchema.Exists)
            RaiseError($"Source table '{Table}' not found.");

        var pkColumns = sourceSchema.Columns.Where(c => c.IsPrimaryKey).ToList();
        if (pkColumns.Count != 1)
            RaiseError($"Entity history requires a single-column PK — '{Table}' has {pkColumns.Count}.");

        var pkColumn = pkColumns[0].Name;
        using var conn = provider.OpenConnection(Database);

        var total = conn.Query<long>(
            $"SELECT COUNT(*) FROM [{historyTable}] WHERE [{pkColumn}] = @pk",
            new DataParameter("pk", Pk)).First();

        // Read rows. Using SELECT * here is safe — the framework provisions
        // the column set and the caller wants the full mirrored row.
        var rows = conn.Query<Dictionary<string, object?>>(
            $"SELECT * FROM [{historyTable}] WHERE [{pkColumn}] = @pk " +
            $"ORDER BY HistoryId DESC",
            new DataParameter("pk", Pk)).Skip(Offset).Take(Limit).ToList();

        var items = new List<Entry>(rows.Count);
        foreach (var row in rows)
        {
            var entry = new Entry();
            foreach (var (key, value) in row)
            {
                switch (key)
                {
                    case "HistoryId": entry.HistoryId = Convert.ToInt64(value); break;
                    case "Operation": entry.Operation = (value as string) ?? ""; break;
                    case "ChangedOn": entry.ChangedOn = value is DateTime dt ? dt : DateTime.Parse((string)value!); break;
                    case "ChangedBy": entry.ChangedBy = (value as string) ?? ""; break;
                    default: entry.State[key] = value; break;
                }
            }
            items.Add(entry);
        }

        return new Result { Items = items, Total = total };
    }
}
