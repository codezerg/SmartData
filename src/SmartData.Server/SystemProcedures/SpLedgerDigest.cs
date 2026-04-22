using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Tracking;

namespace SmartData.Server.SystemProcedures;

/// <summary>
/// Capture the current chain-head digest for a ledgered table — the payload
/// that gets appended to an external anchor store. See spec § Digests &amp;
/// Anchoring.
///
/// <para>
/// Parameters:
///   <list type="bullet">
///     <item><c>table</c> — the source entity's table name (e.g. <c>"Role"</c>). The ledger table is derived as <c>"{table}_Ledger"</c>.</item>
///   </list>
/// </para>
/// </summary>
internal class SpLedgerDigest : SystemStoredProcedure<LedgerDigest>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";

    public override LedgerDigest Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.LedgerRead, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrWhiteSpace(Table))
            RaiseError("Parameter 'table' is required.");

        return db.LedgerDigest($"{Table}_Ledger");
    }
}
