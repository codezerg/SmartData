using System.Buffers.Binary;
using System.Security.Cryptography;
using SmartData.Core.BinarySerialization;

namespace SmartData.Server.Tracking;

/// <summary>
/// Generic ledger-row surface for a ledgered entity <typeparamref name="T"/>.
/// Unlike <see cref="HistoryEntity{T}"/>, the row shape is fixed — entity-shape
/// data lives inside <see cref="CanonicalBytes"/>. <typeparamref name="T"/> is
/// used only for table routing and as the return type of
/// <see cref="Deserialize"/>.
///
/// <para>
/// See <c>docs/SmartData.Server.Tracking.md</c> § Storage → <c>{Table}_Ledger</c>
/// and § Hash Chain.
/// </para>
/// </summary>
public sealed class LedgerEntity<T> where T : class, new()
{
    /// <summary>Chain position within this table (identity).</summary>
    public long LedgerId { get; set; }

    /// <summary>
    /// FK to <c>{Table}_History.HistoryId</c>. NULL for synthetic rows
    /// (prune markers, schema markers). The <c>Operation</c> field inside
    /// <see cref="CanonicalBytes"/> disambiguates synthetic kinds.
    /// </summary>
    public long? HistoryId { get; set; }

    /// <summary>
    /// Version of the canonical serialization format. Currently
    /// <see cref="CurrentFormatVersion"/> (1).
    /// </summary>
    public byte FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>
    /// <c>BinarySerializer.Serialize(canonicalPayload)</c>. Post-image plus
    /// audit metadata (<c>Operation</c>, <c>ChangedOn</c>, <c>ChangedBy</c>) and
    /// every mirrored column. Exact bytes that were hashed.
    /// </summary>
    public byte[] CanonicalBytes { get; set; } = [];

    /// <summary>
    /// <c>RowHash</c> of the previous row in this table's chain. All zeroes for
    /// genesis. The <c>UNIQUE</c> constraint on this column is load-bearing —
    /// see spec § Concurrency.
    /// </summary>
    public byte[] PrevHash { get; set; } = new byte[32];

    /// <summary><c>SHA256(FV ‖ HistoryId ‖ CanonicalBytes ‖ PrevHash)</c>.</summary>
    public byte[] RowHash { get; set; } = new byte[32];

    /// <summary>Current canonical format version. Bump only on binary-incompatible serializer changes.</summary>
    public const byte CurrentFormatVersion = 1;

    /// <summary>
    /// Lazily deserialize <see cref="CanonicalBytes"/> into the canonical
    /// payload wrapper. Throws <see cref="LedgerCorruptBytesException"/> on
    /// malformed bytes and <see cref="LedgerFormatVersionMismatchException"/>
    /// if <see cref="FormatVersion"/> is newer than this build supports.
    /// </summary>
    public LedgerPayload<T> Deserialize()
    {
        if (FormatVersion > CurrentFormatVersion)
            throw new LedgerFormatVersionMismatchException(TableName, LedgerId, FormatVersion);

        try
        {
            var payload = BinarySerializer.Deserialize<LedgerPayload<T>>(CanonicalBytes);
            return payload ?? throw new LedgerCorruptBytesException(TableName, LedgerId);
        }
        catch (LedgerCorruptBytesException) { throw; }
        catch (LedgerFormatVersionMismatchException) { throw; }
        catch (Exception ex) { throw new LedgerCorruptBytesException(TableName, LedgerId, ex); }
    }

    /// <summary>
    /// Recompute <c>SHA256(FV ‖ HistoryId ‖ CanonicalBytes ‖ PrevHash)</c> and
    /// compare to the stored <see cref="RowHash"/>. Pure CPU — no database
    /// round-trip. Does not validate forward links.
    /// </summary>
    public bool VerifySelfHash()
    {
        var computed = ComputeRowHash(FormatVersion, HistoryId, CanonicalBytes, PrevHash);
        return computed.AsSpan().SequenceEqual(RowHash);
    }

    /// <summary>
    /// Canonical hash input encoding — used by both the write path and every
    /// verifier. Matches <c>docs/SmartData.Server.Tracking.md</c> § Hash Chain
    /// → Hash input encoding. Big-endian <c>HistoryId</c>, 32-zero-byte value
    /// for NULL.
    /// </summary>
    public static byte[] ComputeRowHash(byte formatVersion, long? historyId, byte[] canonical, byte[] prev)
    {
        var hid = historyId ?? 0;
        var input = new byte[1 + 8 + canonical.Length + 32];
        input[0] = formatVersion;
        BinaryPrimitives.WriteInt64BigEndian(input.AsSpan(1, 8), hid);
        canonical.CopyTo(input, 9);
        prev.CopyTo(input, 9 + canonical.Length);
        return SHA256.HashData(input);
    }

    // Non-persistent diagnostic companion — set by the read path when
    // instantiating for exception context. Not a mapped column.
    internal string TableName { get; set; } = TrackedEntityInfo<T>.LedgerTableName;
}

/// <summary>
/// The shape serialized into <see cref="LedgerEntity{T}.CanonicalBytes"/>. One
/// wrapper per chain row; audit fields live here so they're covered by the
/// hash. The source entity's mirrored columns are held in <see cref="Data"/>
/// for entity mutations, or <c>null</c> for synthetic rows whose payload lives
/// in <see cref="Synthetic"/>.
/// </summary>
public sealed class LedgerPayload<T> where T : class, new()
{
    /// <summary>Single-character op code: <c>"I"</c> / <c>"U"</c> / <c>"D"</c> / <c>"S"</c> (schema) / <c>"P"</c> (prune). Stored as string — <c>char</c> isn't in the <c>BinarySerializer</c> primitive table.</summary>
    public string Operation { get; set; } = "";
    public DateTime ChangedOn { get; set; }
    public string ChangedBy { get; set; } = "";

    /// <summary>Mirrored entity data for I/U/D rows; <c>null</c> for synthetic (S/P) rows.</summary>
    public T? Data { get; set; }

    /// <summary>Synthetic payload — filled for schema markers (phase 2) and prune markers (phase 5). Null otherwise.</summary>
    public SyntheticPayload? Synthetic { get; set; }
}

/// <summary>
/// Carrier for data specific to synthetic ledger rows (<c>'S'</c> and
/// <c>'P'</c>). Stored inside <c>CanonicalBytes</c>; the outer
/// <see cref="LedgerEntity{T}.HistoryId"/> is NULL for synthetic rows.
/// </summary>
public sealed class SyntheticPayload
{
    /// <summary>Schema marker — captured-set fingerprint + column metadata.</summary>
    public SchemaMarker? Schema { get; set; }
}

/// <summary>
/// Written as the first row of every <c>[Ledger]</c> table (genesis) and on
/// every detected drift of the captured set. See spec § Schema Drift →
/// Marker shape.
/// </summary>
public sealed class SchemaMarker
{
    public DateTime DetectedAt { get; set; }
    public string DetectedBy { get; set; } = "";
    public byte[] CapturedHash { get; set; } = [];
    public CapturedColumn[] Columns { get; set; } = [];
    public string[] Added { get; set; } = [];
    public string[] Removed { get; set; } = [];
    public string? AutoRepoVersion { get; set; }
}

/// <summary>
/// Single entry in the captured-set fingerprint — <c>(ColumnName, ClrType.FullName)</c>.
/// </summary>
public sealed class CapturedColumn
{
    public string Name { get; set; } = "";
    public string ClrType { get; set; } = "";
}
