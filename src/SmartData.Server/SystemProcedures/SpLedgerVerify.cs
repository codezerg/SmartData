using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Tracking;

namespace SmartData.Server.SystemProcedures;

/// <summary>
/// Run integrity verification against a ledgered table.
///
/// <para>
/// Parameters:
///   <list type="bullet">
///     <item><c>table</c> — source entity's table name (the ledger is <c>"{table}_Ledger"</c>).</item>
///     <item><c>anchors</c> — optional list of previously-captured digests. When supplied, the verify
///         additionally matches each anchor's <c>LatestRowHash</c> against the current chain to catch
///         tampering the internal walk cannot see (cascade rewrites, tail truncation, rollback).</item>
///   </list>
/// </para>
/// </summary>
internal class SpLedgerVerify : SystemStoredProcedure<VerificationResult>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public List<LedgerDigest>? Anchors { get; set; }

    public override VerificationResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.LedgerVerify, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrWhiteSpace(Table))
            RaiseError("Parameter 'table' is required.");

        var ledgerTable = $"{Table}_Ledger";
        return Anchors is { Count: > 0 }
            ? db.Verify(ledgerTable, Anchors)
            : db.Verify(ledgerTable);
    }
}
