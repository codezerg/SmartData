using LinqToDB;
using LinqToDB.Data;
using SmartData.Core.BinarySerialization;

namespace SmartData.Server.Tracking;

/// <summary>
/// Raw ledger row — non-generic mirror of <see cref="LedgerEntity{T}"/>. Used
/// by the system procedures (<c>sp_ledger_verify</c>, <c>sp_ledger_digest</c>)
/// which receive a table name at runtime and cannot materialize a closed
/// generic. The shape matches the provisioned <c>{Table}_Ledger</c> exactly.
/// </summary>
internal sealed class LedgerRow
{
    public long LedgerId { get; set; }
    public long? HistoryId { get; set; }
    public byte FormatVersion { get; set; }
    public byte[] CanonicalBytes { get; set; } = [];
    public byte[] PrevHash { get; set; } = [];
    public byte[] RowHash { get; set; } = [];
}

/// <summary>
/// Forward-walk verification over a single ledger table. Spec § Read Path →
/// <c>ctx.Verify&lt;T&gt;</c>: starting from the oldest surviving row and
/// proceeding by ascending <c>LedgerId</c>, check each row's self-hash and
/// forward link to the next row.
///
/// <para>
/// Internal consistency catches broken forward-links, mismatched self-hashes,
/// and reordered rows. It does <b>not</b> catch a consistent cascade rewrite
/// (privileged rewrite of row N forward with every hash updated along the
/// way) or tail truncation — those require anchored verification.
/// </para>
/// </summary>
internal static class LedgerVerifier
{
    public static VerificationResult Verify<T>(DataConnection conn) where T : class, new()
        => VerifyInternal(ReadAllRows<T>(conn));

    public static VerificationResult VerifyByTableName(DataConnection conn, string ledgerTableName)
        => VerifyInternal(ReadAllRowsRaw(conn, ledgerTableName));

    private static VerificationResult VerifyInternal(List<LedgerRow> rows)
    {
        if (rows.Count == 0)
        {
            return new VerificationResult
            {
                Valid = true,
                InternalConsistencyValid = true,
                EntriesVerified = 0,
            };
        }

        long verified = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var recomputed = LedgerEntity<object>.ComputeRowHash(
                row.FormatVersion, row.HistoryId, row.CanonicalBytes, row.PrevHash);
            if (!recomputed.AsSpan().SequenceEqual(row.RowHash))
            {
                return new VerificationResult
                {
                    Valid = false,
                    InternalConsistencyValid = false,
                    FirstBadLedgerId = row.LedgerId,
                    FailureKind = VerificationFailure.RowHashMismatch,
                    EntriesVerified = verified,
                };
            }

            if (i + 1 < rows.Count)
            {
                var next = rows[i + 1];
                if (!next.PrevHash.AsSpan().SequenceEqual(row.RowHash))
                {
                    return new VerificationResult
                    {
                        Valid = false,
                        InternalConsistencyValid = false,
                        FirstBadLedgerId = next.LedgerId,
                        FailureKind = VerificationFailure.PrevHashBroken,
                        EntriesVerified = verified + 1,
                    };
                }
            }
            verified++;
        }

        return new VerificationResult
        {
            Valid = true,
            InternalConsistencyValid = true,
            EntriesVerified = verified,
        };
    }

    private static List<LedgerRow> ReadAllRows<T>(DataConnection conn) where T : class, new()
        => conn.GetTable<LedgerEntity<T>>()
               .OrderBy(l => l.LedgerId)
               .Select(l => new LedgerRow
               {
                   LedgerId = l.LedgerId,
                   HistoryId = l.HistoryId,
                   FormatVersion = l.FormatVersion,
                   CanonicalBytes = l.CanonicalBytes,
                   PrevHash = l.PrevHash,
                   RowHash = l.RowHash,
               })
               .ToList();

    private static List<LedgerRow> ReadAllRowsRaw(DataConnection conn, string ledgerTableName)
        => conn.Query<LedgerRow>(
            $"SELECT LedgerId, HistoryId, FormatVersion, CanonicalBytes, PrevHash, RowHash " +
            $"FROM [{ledgerTableName}] ORDER BY LedgerId").ToList();

    public static VerificationResult VerifyAnchored<T>(DataConnection conn, IEnumerable<LedgerDigest> anchors)
        where T : class, new()
        => VerifyAnchoredInternal(
            Verify<T>(conn), anchors,
            () => conn.GetTable<LedgerEntity<T>>().Select(l => (long?)l.LedgerId).Max() ?? 0L,
            id => conn.GetTable<LedgerEntity<T>>().Where(l => l.LedgerId == id).Select(l => l.RowHash).FirstOrDefault());

    public static VerificationResult VerifyAnchoredByTableName(DataConnection conn, string ledgerTableName,
        IEnumerable<LedgerDigest> anchors)
        => VerifyAnchoredInternal(
            VerifyByTableName(conn, ledgerTableName), anchors,
            () => conn.Query<long?>($"SELECT MAX(LedgerId) FROM [{ledgerTableName}]").FirstOrDefault() ?? 0L,
            id => conn.Query<byte[]>($"SELECT RowHash FROM [{ledgerTableName}] WHERE LedgerId = {id}").FirstOrDefault());

    private static VerificationResult VerifyAnchoredInternal(
        VerificationResult internalResult,
        IEnumerable<LedgerDigest> anchors,
        Func<long> readMaxLedgerId,
        Func<long, byte[]?> readRowHashAt)
    {
        var anchorList = anchors.OrderBy(a => a.LatestLedgerId).ToList();
        if (anchorList.Count == 0) return internalResult;

        var maxLedgerId = readMaxLedgerId();

        var failures = new List<AnchorFailure>();
        int matched = 0;
        int superseded = 0;
        DateTime? lastGood = null;
        DateTime? earliestBad = null;

        foreach (var anchor in anchorList)
        {
            if (anchor.LatestLedgerId > maxLedgerId)
            {
                failures.Add(new AnchorFailure
                {
                    AnchorTime = anchor.ChangedOn,
                    AnchorLedgerId = anchor.LatestLedgerId,
                    ExpectedHash = anchor.LatestRowHash,
                    ActualHash = null,
                    Kind = AnchorFailureKind.ChainRegression,
                });
                earliestBad ??= anchor.ChangedOn;
                continue;
            }

            var actualHash = readRowHashAt(anchor.LatestLedgerId);
            if (actualHash is null)
            {
                // Phase 5 will distinguish "superseded by prune" from raw "missing";
                // phase 3 has no prune markers to check against.
                failures.Add(new AnchorFailure
                {
                    AnchorTime = anchor.ChangedOn,
                    AnchorLedgerId = anchor.LatestLedgerId,
                    ExpectedHash = anchor.LatestRowHash,
                    ActualHash = null,
                    Kind = AnchorFailureKind.RowMissing,
                });
                earliestBad ??= anchor.ChangedOn;
                continue;
            }

            if (actualHash.AsSpan().SequenceEqual(anchor.LatestRowHash))
            {
                matched++;
                lastGood = anchor.ChangedOn;
            }
            else
            {
                failures.Add(new AnchorFailure
                {
                    AnchorTime = anchor.ChangedOn,
                    AnchorLedgerId = anchor.LatestLedgerId,
                    ExpectedHash = anchor.LatestRowHash,
                    ActualHash = actualHash,
                    Kind = AnchorFailureKind.RowHashMismatch,
                });
                earliestBad ??= anchor.ChangedOn;
            }
        }

        var valid = internalResult.InternalConsistencyValid && failures.Count == 0;

        return new VerificationResult
        {
            Valid = valid,
            InternalConsistencyValid = internalResult.InternalConsistencyValid,
            FirstBadLedgerId = internalResult.FirstBadLedgerId,
            FailureKind = internalResult.FailureKind,
            EntriesVerified = internalResult.EntriesVerified,
            AnchorsChecked = anchorList.Count,
            AnchorsMatched = matched,
            AnchorsSuperseded = superseded,
            AnchorFailures = failures,
            LastKnownGood = lastGood,
            EarliestTamper = earliestBad,
        };
    }

    public static LedgerDigest ComputeDigest<T>(DataConnection conn) where T : class, new()
        => ComputeDigestByTableName(conn, TrackedEntityInfo<T>.LedgerTableName);

    /// <summary>
    /// Table-name based digest — the form called by <c>sp_ledger_digest</c>.
    /// Reads the head row, its hash, and the row count. For entity rows the
    /// timestamp comes from the paired history row (keeps the digest
    /// type-agnostic); for synthetic rows we decode the payload.
    /// </summary>
    public static LedgerDigest ComputeDigestByTableName(DataConnection conn, string ledgerTableName)
    {
        // Provider-agnostic "head of chain" read — avoids LIMIT/TOP divergence
        // between SQLite and SQL Server by filtering on MAX(LedgerId).
        var rows = conn.Query<LedgerRow>(
            $"SELECT LedgerId, HistoryId, FormatVersion, CanonicalBytes, PrevHash, RowHash " +
            $"FROM [{ledgerTableName}] " +
            $"WHERE LedgerId = (SELECT MAX(LedgerId) FROM [{ledgerTableName}])").ToList();

        if (rows.Count == 0)
            return new LedgerDigest { TableName = ledgerTableName };

        var head = rows[0];
        var count = conn.Query<long>($"SELECT COUNT(*) FROM [{ledgerTableName}]").First();
        var ts = ResolveDigestTimestamp(conn, ledgerTableName, head);

        return new LedgerDigest
        {
            TableName = ledgerTableName,
            LatestLedgerId = head.LedgerId,
            LatestRowHash = head.RowHash,
            ChangedOn = ts,
            EntryCount = count,
        };
    }

    private static DateTime ResolveDigestTimestamp(DataConnection conn, string ledgerTableName, LedgerRow head)
    {
        // Entity row: look up paired history row's ChangedOn. Avoids having
        // to know T to deserialize the canonical bytes.
        if (head.HistoryId is long hid)
        {
            var historyTable = DeriveHistoryTableName(ledgerTableName);
            try
            {
                var dt = conn.Query<DateTime>(
                    $"SELECT ChangedOn FROM [{historyTable}] WHERE HistoryId = {hid}").FirstOrDefault();
                if (dt != default) return dt;
            }
            catch { /* fall through */ }
        }

        // Synthetic row (HistoryId IS NULL) — Data is null, so LedgerPayload<object>
        // decode works without knowing T.
        try
        {
            var payload = BinarySerializer.Deserialize<LedgerPayload<object>>(head.CanonicalBytes);
            if (payload is not null) return payload.ChangedOn;
        }
        catch { /* intentional fall-through */ }
        return DateTime.MinValue;
    }

    private static string DeriveHistoryTableName(string ledgerTable)
        => ledgerTable.EndsWith("_Ledger", StringComparison.OrdinalIgnoreCase)
            ? ledgerTable[..^"_Ledger".Length] + "_History"
            : ledgerTable + "_History";
}
