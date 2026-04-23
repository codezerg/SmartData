using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LinqToDB;
using LinqToDB.Data;
using SmartData.Core.BinarySerialization;

namespace SmartData.Server.Tracking;

/// <summary>
/// Writes ledger rows for a single entity mutation — the hot path documented
/// in <c>docs/SmartData.Server.Tracking.md</c> § Write Path.
///
/// <para>
/// One instance per scope (held by <see cref="TrackingWritePath"/>). Holds a
/// cache of which <c>(dbName, ledgerTable)</c> pairs have already had their
/// genesis schema marker written during this process lifetime — the empty-
/// ledger check is itself cheap but adding the flag saves a round-trip on
/// every subsequent write.
/// </para>
/// </summary>
internal sealed class LedgerWriter
{
    private const int MaxRetryAttempts = 10;
    private readonly ITrackingUserProvider _userProvider;
    private readonly ConcurrentDictionary<string, byte> _driftChecked = new();

    public LedgerWriter(ITrackingUserProvider userProvider)
    {
        _userProvider = userProvider;
    }

    /// <summary>
    /// Append a ledger row paired with the given history row. Runs inside the
    /// caller's transaction (started by <see cref="TrackingWritePath"/>). Before
    /// the first entity write per (connection, ledger table) this also takes
    /// care of genesis and schema-drift markers (spec § Schema Drift).
    /// </summary>
    public void AppendEntityRow<T>(DataConnection conn, long historyId, T data,
        string operation, DateTime changedOn, string changedBy) where T : class, new()
    {
        EnsureSchemaBaseline<T>(conn);

        var payload = new LedgerPayload<T>
        {
            Operation = operation,
            ChangedOn = changedOn,
            ChangedBy = changedBy,
            Data = data,
        };
        var canonicalBytes = BinarySerializer.Serialize(payload);

        InsertWithRetry<T>(conn, historyId, canonicalBytes);
    }

    /// <summary>
    /// Ensures the ledger has an up-to-date <c>'S'</c> marker before the next
    /// entity row is appended. Handles three cases:
    /// <list type="bullet">
    ///   <item><b>Empty ledger</b> — writes genesis marker (spec § Schema Drift → Genesis).</item>
    ///   <item><b>Captured set matches the latest marker</b> — no-op.</item>
    ///   <item><b>Drift</b> — writes a new <c>'S'</c> marker with the symmetric-diff <c>Added</c>/<c>Removed</c> arrays.</item>
    /// </list>
    /// Idempotent per (connection, ledger table) — the outcome is cached once
    /// observed. Race with another instance is handled by the
    /// <c>UNIQUE(PrevHash)</c> retry in the inner writers.
    /// </summary>
    private void EnsureSchemaBaseline<T>(DataConnection conn) where T : class, new()
    {
        var key = CacheKey<T>(conn);
        if (_driftChecked.ContainsKey(key)) return;

        if (ReadHeadRowHash<T>(conn) is null)
        {
            WriteGenesisSchemaMarker<T>(conn);
            _driftChecked.TryAdd(key, 0);
            return;
        }

        var latest = ReadLatestSchemaMarker<T>(conn);
        var (currentHash, currentColumns) = BuildCapturedSet<T>();

        if (latest is null)
        {
            // Ledger has rows but no prior 'S' marker — upgrade from a pre-
            // drift-detection build. Spec § Schema Drift → Retroactive markers.
            // Best we can do is write the current set as the baseline.
            WriteDriftSchemaMarker<T>(conn, currentHash, currentColumns, added: [], removed: []);
            _driftChecked.TryAdd(key, 0);
            return;
        }

        if (currentHash.AsSpan().SequenceEqual(latest.CapturedHash))
        {
            _driftChecked.TryAdd(key, 0);
            return;
        }

        var prevNames = latest.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var currentNames = currentColumns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var added = currentNames.Except(prevNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        var removed = prevNames.Except(currentNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        WriteDriftSchemaMarker<T>(conn, currentHash, currentColumns, added, removed);
        _driftChecked.TryAdd(key, 0);
    }

    /// <summary>
    /// Walks back from the chain head through synthetic rows until the most
    /// recent <c>'S'</c> marker is found. Skips <c>'P'</c> prune markers
    /// defensively (they arrive in phase 5). Returns null on an empty chain or
    /// if no <c>'S'</c> marker exists.
    /// </summary>
    private static SchemaMarker? ReadLatestSchemaMarker<T>(DataConnection conn) where T : class, new()
    {
        // Synthetic rows only — entity rows have non-null HistoryId.
        var synth = conn.GetTable<LedgerEntity<T>>()
            .Where(l => l.HistoryId == null)
            .OrderByDescending(l => l.LedgerId)
            .Take(10) // defensive upper bound
            .ToList();

        foreach (var row in synth)
        {
            try
            {
                var payload = row.Deserialize();
                if (payload.Operation == "S" && payload.Synthetic?.Schema is { } marker)
                    return marker;
            }
            catch { /* skip unreadable synthetic row */ }
        }
        return null;
    }

    private void WriteGenesisSchemaMarker<T>(DataConnection conn) where T : class, new()
    {
        var (hash, columns) = BuildCapturedSet<T>();
        try
        {
            WriteSchemaMarkerRow<T>(conn, hash, columns, added: [], removed: [], prev: new byte[32]);
        }
        catch (Exception ex) when (IsUniqueViolation(ex))
        {
            // Another writer raced us to genesis and won. Their marker is in
            // place — verify it matches our set (spec § Schema Drift → Concurrent
            // startup across instances). If it doesn't, drift detection will
            // run the next time through EnsureSchemaBaseline.
        }
    }

    private void WriteDriftSchemaMarker<T>(DataConnection conn,
        byte[] capturedHash, CapturedColumn[] columns, string[] added, string[] removed) where T : class, new()
    {
        // Chain on top of current head — retries handle races with concurrent writers.
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            var prev = ReadHeadRowHash<T>(conn) ?? new byte[32];
            try
            {
                WriteSchemaMarkerRow<T>(conn, capturedHash, columns, added, removed, prev);
                return;
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                // Another writer appended between our head-read and insert — if they
                // happened to write an 'S' marker with the same CapturedHash, we're
                // done. Otherwise retry against the new head.
                var latest = ReadLatestSchemaMarker<T>(conn);
                if (latest is not null && latest.CapturedHash.AsSpan().SequenceEqual(capturedHash))
                    return;
            }
        }

        throw new LedgerRetryExhaustedException(
            TrackedEntityInfo<T>.LedgerTableName, MaxRetryAttempts);
    }

    private void WriteSchemaMarkerRow<T>(DataConnection conn,
        byte[] capturedHash, CapturedColumn[] columns, string[] added, string[] removed, byte[] prev)
        where T : class, new()
    {
        var now = DateTime.UtcNow;
        var user = _userProvider.CurrentUser;
        var marker = new SchemaMarker
        {
            DetectedAt = now,
            DetectedBy = user,
            CapturedHash = capturedHash,
            Columns = columns,
            Added = added,
            Removed = removed,
            AutoRepoVersion = null,
        };
        var payload = new LedgerPayload<T>
        {
            Operation = "S",
            ChangedOn = now,
            ChangedBy = user,
            Data = null,
            Synthetic = new SyntheticPayload { Schema = marker },
        };
        var canonical = BinarySerializer.Serialize(payload);
        var rowHash = LedgerEntity<T>.ComputeRowHash(
            LedgerEntity<T>.CurrentFormatVersion, historyId: null, canonical, prev);

        conn.Insert(new LedgerEntity<T>
        {
            HistoryId = null,
            FormatVersion = LedgerEntity<T>.CurrentFormatVersion,
            CanonicalBytes = canonical,
            PrevHash = prev,
            RowHash = rowHash,
        }, tableName: TrackedEntityInfo<T>.LedgerTableName);
    }

    private void InsertWithRetry<T>(DataConnection conn, long historyId, byte[] canonical) where T : class, new()
    {
        for (int attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            var prev = ReadHeadRowHash<T>(conn) ?? new byte[32];
            var rowHash = LedgerEntity<T>.ComputeRowHash(
                LedgerEntity<T>.CurrentFormatVersion, historyId, canonical, prev);

            var row = new LedgerEntity<T>
            {
                HistoryId = historyId,
                FormatVersion = LedgerEntity<T>.CurrentFormatVersion,
                CanonicalBytes = canonical,
                PrevHash = prev,
                RowHash = rowHash,
            };

            try
            {
                conn.Insert(row, tableName: TrackedEntityInfo<T>.LedgerTableName);
                return;
            }
            catch (Exception ex) when (IsUniqueViolation(ex))
            {
                // Another writer appended between our head-read and insert.
                // Re-read and retry with the fresh PrevHash.
            }
        }

        throw new LedgerRetryExhaustedException(
            TrackedEntityInfo<T>.LedgerTableName, MaxRetryAttempts);
    }

    /// <summary>Reads the current chain head's <c>RowHash</c>, or null if empty.</summary>
    private static byte[]? ReadHeadRowHash<T>(DataConnection conn) where T : class, new()
    {
        var table = conn.GetTable<LedgerEntity<T>>();
        return table
            .OrderByDescending(l => l.LedgerId)
            .Select(l => l.RowHash)
            .FirstOrDefault();
    }

    private static string CacheKey<T>(DataConnection conn) where T : class, new()
        => $"{conn.ConnectionString}::{TrackedEntityInfo<T>.LedgerTableName}";

    /// <summary>
    /// Build the captured-set fingerprint — spec § Schema Drift → Captured set.
    /// Sorted (ColumnName, ClrType.FullName) pairs, canonicalised as UTF-8
    /// <c>name:type\n</c>, SHA-256.
    /// </summary>
    private static (byte[] hash, CapturedColumn[] columns) BuildCapturedSet<T>() where T : class, new()
    {
        var ordered = TrackedEntityInfo<T>.MirroredProperties
            .Select(p => new CapturedColumn
            {
                Name = p.Name,
                ClrType = p.PropertyType.FullName ?? p.PropertyType.Name,
            })
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToArray();

        var sb = new StringBuilder(ordered.Length * 32);
        foreach (var col in ordered)
        {
            sb.Append(col.Name).Append(':').Append(col.ClrType).Append('\n');
        }
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return (hash, ordered);
    }

    /// <summary>
    /// Cross-provider detection of a <c>UNIQUE</c>-constraint violation. Both
    /// SQLite (<c>SQLITE_CONSTRAINT</c>) and SQL Server (errors 2627/2601) emit
    /// identifiable messages in the default ADO.NET wrapping. Intentionally
    /// loose: if a non-unique error sneaks through, the retry cap will catch it
    /// and surface as <see cref="LedgerRetryExhaustedException"/> — noisy but
    /// not data-destroying.
    /// </summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            var msg = cur.Message ?? "";
            if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Violation of UNIQUE", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
