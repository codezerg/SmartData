using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

/// <summary>
/// Downgrade a ledgered entity to plain tracking. Drops <c>{Table}_Ledger</c>;
/// <c>{Table}_History</c> remains, and writes continue to append to it, but
/// all prior integrity claims for this table are permanently void — any
/// existing JSONL digest archives stop anchoring a live chain.
///
/// <para>
/// <b>Pre-condition:</b> the developer must change <c>[Ledger]</c> →
/// <c>[Tracked]</c> (or remove the attribute — it goes sticky-tracked) before
/// or alongside the drop. Otherwise sticky resolution observes
/// <c>[Ledger]</c> + <c>_History</c> ✓ + no <c>_Ledger</c> and re-provisions
/// a fresh ledger at the next startup, starting a new chain at genesis with
/// no claim of continuity with the dropped one.
/// </para>
///
/// <para>
/// <b>Limitation (v1):</b> process restart required before next write to the
/// downgraded entity — per-type schema caches don't invalidate.
/// </para>
///
/// <para>
/// Parameters: <c>database</c>, <c>table</c>, <c>confirm</c> (must equal
/// <c>table</c>).
/// </para>
/// </summary>
internal class SpLedgerDrop : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Confirm { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.LedgerAll, Database);
        db.UseDatabase(Database);
        SpTrackingDrop.ValidateConfirm(Table, Confirm);

        var ledgerTable = $"{Table}_Ledger";
        if (!provider.Schema.GetTableSchema(Database, ledgerTable).Exists)
            RaiseError($"No ledger table '{ledgerTable}' in database '{Database}'.");

        provider.SchemaOperations.DropTable(Database, ledgerTable);
        return $"Dropped {ledgerTable}. All prior integrity claims for '{Table}' are void. " +
               $"Update [Ledger] → [Tracked] in code before next startup, or sticky resolution will re-provision the ledger.";
    }
}
