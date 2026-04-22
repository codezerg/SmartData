using System.Security.Cryptography;
using System.Text;
using LinqToDB;
using LinqToDB.Data;
using SmartData.Core.BinarySerialization;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Tracking;

namespace SmartData.Server.SystemProcedures;

/// <summary>
/// Prune a ledgered table — delete history + ledger rows older than
/// <c>olderThan</c>, inside a single transaction, recording the prune itself
/// as a synthetic <c>'P'</c> ledger row that chains into the surviving
/// sequence.
///
/// <para>
/// Sequence (spec § Pruning → <c>sp_ledger_prune</c>):
/// </para>
/// <list type="number">
///   <item>Find <c>boundary = MAX(LedgerId)</c> among ledger rows paired with history rows whose <c>ChangedOn &lt; olderThan</c>.</item>
///   <item>Verify internal chain consistency to <c>boundary</c>, fail fast on pre-existing corruption.</item>
///   <item>Append a <c>'P'</c> marker chained to the current head (captures the prune metadata).</item>
///   <item>Delete ledger rows <c>LedgerId &lt;= boundary</c> — including any <c>'S'</c> markers in that range. The chain invariant requires contiguous deletion (spec § Pruning).</item>
///   <item>Delete paired history rows.</item>
///   <item>Commit.</item>
/// </list>
///
/// <para>Parameters: <c>database</c>, <c>table</c>, <c>olderThan</c>.</para>
/// </summary>
internal class SpLedgerPrune : SystemStoredProcedure<SpLedgerPrune.Result>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public DateTime OlderThan { get; set; }

    public sealed class Result
    {
        public string TableName { get; set; } = "";
        public long BoundaryLedgerId { get; set; }
        public long LedgerRowsDeleted { get; set; }
        public long HistoryRowsDeleted { get; set; }
        public long PruneMarkerLedgerId { get; set; }
    }

    public override Result Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.LedgerAll, Database);
        db.UseDatabase(Database);

        var historyTable = $"{Table}_History";
        var ledgerTable = $"{Table}_Ledger";

        if (!provider.Schema.GetTableSchema(Database, ledgerTable).Exists)
            RaiseError($"No ledger table '{ledgerTable}' — use sp_history_prune for [Tracked]-only entities.");

        using var conn = provider.OpenConnection(Database);
        using var tx = conn.BeginTransaction();

        // 1. Find boundary.
        var olderThanIso = OlderThan.ToUniversalTime().ToString("o");
        var boundaryRaw = conn.Query<long?>(
            $"SELECT MAX(l.LedgerId) FROM [{ledgerTable}] l " +
            $"INNER JOIN [{historyTable}] h ON h.HistoryId = l.HistoryId " +
            $"WHERE h.ChangedOn < @olderThan",
            new DataParameter("olderThan", olderThanIso)).FirstOrDefault();

        if (boundaryRaw is not long boundary)
        {
            tx.Rollback();
            return new Result { TableName = ledgerTable, BoundaryLedgerId = 0 };
        }

        // 2. Verify chain up to boundary — fail fast on pre-existing corruption.
        var preCheck = LedgerVerifier.VerifyByTableName(conn, ledgerTable);
        if (!preCheck.InternalConsistencyValid)
            RaiseError(
                $"Pre-prune verification of '{ledgerTable}' failed at LedgerId {preCheck.FirstBadLedgerId} ({preCheck.FailureKind}). " +
                "Fix the chain before pruning — prune preserves integrity by design.");

        // 3. Build prune metadata and append 'P' marker chained to current head.
        var headHash = conn.Query<byte[]?>(
            $"SELECT RowHash FROM [{ledgerTable}] WHERE LedgerId = (SELECT MAX(LedgerId) FROM [{ledgerTable}])")
            .FirstOrDefault() ?? new byte[32];

        var boundaryHash = conn.Query<byte[]?>(
            $"SELECT RowHash FROM [{ledgerTable}] WHERE LedgerId = {boundary}").FirstOrDefault()
            ?? new byte[32];

        var metadata = new PruneMarker
        {
            PrunedFrom = 1,
            PrunedTo = boundary,
            BoundaryRowHash = boundaryHash,
            PrunedAt = DateTime.UtcNow,
            PrunedBy = db.Services.GetService(typeof(ITrackingUserProvider)) is ITrackingUserProvider u
                ? u.CurrentUser : "system",
            ArchiveRef = null,
        };
        var marker = new LedgerPruneEnvelope
        {
            Operation = "P",
            ChangedOn = metadata.PrunedAt,
            ChangedBy = metadata.PrunedBy,
            Prune = metadata,
        };
        var canonical = BinarySerializer.Serialize(marker);
        var rowHash = ComputeHash(1, null, canonical, headHash);

        conn.Execute(
            $"INSERT INTO [{ledgerTable}] (HistoryId, FormatVersion, CanonicalBytes, PrevHash, RowHash) " +
            $"VALUES (NULL, 1, @cb, @prev, @hash)",
            new DataParameter("cb", canonical),
            new DataParameter("prev", headHash),
            new DataParameter("hash", rowHash));

        var markerLedgerId = conn.Query<long>(
            $"SELECT LedgerId FROM [{ledgerTable}] WHERE RowHash = @hash",
            new DataParameter("hash", rowHash)).First();

        // 4. Delete paired history rows (read their HistoryIds first so step 5
        // can wipe them after ledger delete cascades correctly).
        var historyIdsToDelete = conn.Query<long>(
            $"SELECT HistoryId FROM [{ledgerTable}] WHERE LedgerId <= {boundary} AND HistoryId IS NOT NULL")
            .ToList();

        long histDeleted = 0;
        if (historyIdsToDelete.Count > 0)
        {
            var idList = string.Join(",", historyIdsToDelete);
            histDeleted = conn.Execute(
                $"DELETE FROM [{historyTable}] WHERE HistoryId IN ({idList})");
        }

        // 5. Delete ledger rows <= boundary.
        var ledgerDeleted = conn.Execute(
            $"DELETE FROM [{ledgerTable}] WHERE LedgerId <= {boundary}");

        tx.Commit();

        return new Result
        {
            TableName = ledgerTable,
            BoundaryLedgerId = boundary,
            LedgerRowsDeleted = ledgerDeleted,
            HistoryRowsDeleted = histDeleted,
            PruneMarkerLedgerId = markerLedgerId,
        };
    }

    private static byte[] ComputeHash(byte formatVersion, long? historyId, byte[] canonical, byte[] prev)
    {
        var hid = historyId ?? 0;
        var input = new byte[1 + 8 + canonical.Length + 32];
        input[0] = formatVersion;
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(input.AsSpan(1, 8), hid);
        canonical.CopyTo(input, 9);
        prev.CopyTo(input, 9 + canonical.Length);
        return SHA256.HashData(input);
    }
}

/// <summary>
/// Payload serialized into a <c>'P'</c> prune-marker ledger row. Dedicated
/// shape (not <see cref="LedgerPayload{T}"/>) because the row never carries
/// source-entity data and doesn't need to be type-parameterized.
/// </summary>
public sealed class LedgerPruneEnvelope
{
    public string Operation { get; set; } = "";
    public DateTime ChangedOn { get; set; }
    public string ChangedBy { get; set; } = "";
    public PruneMarker Prune { get; set; } = new();
}

public sealed class PruneMarker
{
    public long PrunedFrom { get; set; }
    public long PrunedTo { get; set; }
    public byte[] BoundaryRowHash { get; set; } = [];
    public DateTime PrunedAt { get; set; }
    public string PrunedBy { get; set; } = "";
    public string? ArchiveRef { get; set; }
}
