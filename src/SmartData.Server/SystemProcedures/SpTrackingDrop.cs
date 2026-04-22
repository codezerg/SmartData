using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

/// <summary>
/// Drop all tracking state for an entity — both <c>{Table}_History</c> and
/// <c>{Table}_Ledger</c>. Destroys queryable history and voids all ledger
/// integrity claims. Irreversible from the framework's perspective.
///
/// <para>
/// Retain JSONL digest archives and <c>sp_ledger_prune</c> output separately
/// if the data may later need to be defended — they become unverifiable
/// against this chain once the ledger is dropped.
/// </para>
///
/// <para>
/// <b>Limitation (v1):</b> after drop, the process must be restarted before
/// writing to the dropped entity again. The per-type static schema caches
/// don't get invalidated by this procedure, so a subsequent write would skip
/// re-provisioning the history table.
/// </para>
///
/// <para>
/// Parameters: <c>database</c>, <c>table</c>, <c>confirm</c> (must equal
/// <c>table</c> — typo guard enforced inside the procedure body).
/// </para>
/// </summary>
internal class SpTrackingDrop : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Confirm { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.LedgerAll, Database);
        db.UseDatabase(Database);
        ValidateConfirm(Table, Confirm);

        var historyTable = $"{Table}_History";
        var ledgerTable = $"{Table}_Ledger";
        var dropped = new List<string>();

        var schema = provider.Schema;
        var ops = provider.SchemaOperations;

        if (schema.GetTableSchema(Database, ledgerTable).Exists)
        {
            ops.DropTable(Database, ledgerTable);
            dropped.Add(ledgerTable);
        }

        if (schema.GetTableSchema(Database, historyTable).Exists)
        {
            ops.DropTable(Database, historyTable);
            dropped.Add(historyTable);
        }

        if (dropped.Count == 0)
            RaiseError($"No tracking tables found for '{Table}' in database '{Database}'.");

        return $"Dropped {string.Join(", ", dropped)}. Restart the process before writing to '{Table}' again.";
    }

    /// <summary>
    /// Enforced inside the procedure body so a typo-protection guard actually
    /// guards — not just at whatever CLI/UI called the procedure. See spec
    /// § Lifecycle → Disabling tracking.
    /// </summary>
    internal static void ValidateConfirm(string table, string confirm)
    {
        if (!string.Equals(table, confirm, StringComparison.Ordinal))
            throw new ArgumentException(
                $"'confirm' must equal 'table' (got confirm='{confirm}', table='{table}'). " +
                "This guard exists to prevent accidental drops — retype the table name exactly.");
    }
}
