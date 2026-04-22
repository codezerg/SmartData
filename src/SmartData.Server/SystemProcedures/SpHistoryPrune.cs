using LinqToDB.Data;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

/// <summary>
/// Delete history rows older than the supplied timestamp from a
/// <c>[Tracked]</c>-only table. A plain <c>DELETE</c>: safe, idempotent,
/// provider-neutral.
///
/// <para>
/// <b>Refused on <c>[Ledger]</c> tables.</b> The history + ledger tables
/// must prune together (via <c>sp_ledger_prune</c>) so the prune marker
/// ends up in the chain and the paired <c>HistoryId</c> values stay
/// consistent.
/// </para>
///
/// <para>
/// Parameters: <c>database</c>, <c>table</c>, <c>olderThan</c> (UTC
/// timestamp; rows with <c>ChangedOn &lt; olderThan</c> are deleted).
/// </para>
/// </summary>
internal class SpHistoryPrune : SystemStoredProcedure<SpHistoryPrune.Result>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public DateTime OlderThan { get; set; }

    public sealed class Result
    {
        public string TableName { get; set; } = "";
        public long RowsDeleted { get; set; }
    }

    public override Result Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.LedgerAll, Database);
        db.UseDatabase(Database);

        var historyTable = $"{Table}_History";
        var ledgerTable = $"{Table}_Ledger";

        if (provider.Schema.GetTableSchema(Database, ledgerTable).Exists)
            RaiseError($"'{Table}' is ledgered. Use sp_ledger_prune so the ledger + history stay consistent.");

        if (!provider.Schema.GetTableSchema(Database, historyTable).Exists)
            RaiseError($"No history table '{historyTable}' in database '{Database}'.");

        using var conn = provider.OpenConnection(Database);
        var isoTimestamp = OlderThan.ToUniversalTime().ToString("o");
        var affected = conn.Execute(
            $"DELETE FROM [{historyTable}] WHERE ChangedOn < @olderThan",
            new DataParameter("olderThan", isoTimestamp));

        return new Result { TableName = historyTable, RowsDeleted = affected };
    }
}
