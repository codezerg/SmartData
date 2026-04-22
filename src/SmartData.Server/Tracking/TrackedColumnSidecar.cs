using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Logging;
using SmartData.Server.Entities;

namespace SmartData.Server.Tracking;

/// <summary>
/// Drift detection for <c>[Tracked]</c>-only entities. No chain exists on
/// these tables, so integrity isn't a concern — the sidecar exists only so
/// operators can grep logs and see "the captured set on <c>Customer</c>
/// changed between release X and Y." See spec § Schema Drift →
/// <c>[Tracked]</c>-only tables.
///
/// <para>
/// Called once per (connection, entity) in a process lifetime. On first
/// observation writes a baseline row; on subsequent observations with a
/// different captured set, logs a WARN with the <c>Added</c>/<c>Removed</c>
/// diff and updates the row.
/// </para>
/// </summary>
internal sealed class TrackedColumnSidecar
{
    private readonly ILogger<TrackedColumnSidecar>? _logger;
    private readonly ConcurrentDictionary<string, byte> _checked = new();

    public TrackedColumnSidecar(ILogger<TrackedColumnSidecar>? logger = null)
    {
        _logger = logger;
    }

    public void CheckDrift<T>(DataConnection conn, ITrackingUserProvider userProvider) where T : class, new()
    {
        var key = $"{conn.ConnectionString}::{TrackedEntityInfo<T>.HistoryTableName}";
        if (_checked.ContainsKey(key)) return;

        var columns = TrackedEntityInfo<T>.MirroredProperties
            .Select(p => new CapturedColumn
            {
                Name = p.Name,
                ClrType = p.PropertyType.FullName ?? p.PropertyType.Name,
            })
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();

        var (hash, json) = Fingerprint(columns);
        var tableName = EntityMapping<T>.GetTableName();

        var sidecarTable = conn.GetTable<SysTrackedColumns>();
        var existing = sidecarTable.FirstOrDefault(r => r.TableName == tableName);

        if (existing is null)
        {
            // First observation — write baseline. Best-effort: if another
            // instance wrote concurrently, the PK will catch it; we ignore.
            try
            {
                conn.Insert(new SysTrackedColumns
                {
                    TableName = tableName,
                    CapturedColumnsJson = json,
                    CapturedHash = hash,
                    StampedOn = DateTime.UtcNow,
                    StampedBy = userProvider.CurrentUser,
                });
            }
            catch (Exception ex) when (IsUniqueOrDuplicate(ex))
            {
                // Another writer beat us to baseline — ignore.
            }
            _checked.TryAdd(key, 0);
            return;
        }

        if (hash.AsSpan().SequenceEqual(existing.CapturedHash))
        {
            _checked.TryAdd(key, 0);
            return;
        }

        // Drift observed. Log + update baseline.
        var prevNames = ExtractNames(existing.CapturedColumnsJson).ToHashSet(StringComparer.Ordinal);
        var currNames = columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var added = string.Join(", ", currNames.Except(prevNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal));
        var removed = string.Join(", ", prevNames.Except(currNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal));

        _logger?.LogWarning(
            "Captured-set drift on [Tracked] entity '{Table}': added=[{Added}] removed=[{Removed}]. " +
            "Updating sidecar baseline. (No integrity claim — use [Ledger] for audit-grade drift history.)",
            tableName, added, removed);

        existing.CapturedColumnsJson = json;
        existing.CapturedHash = hash;
        existing.StampedOn = DateTime.UtcNow;
        existing.StampedBy = userProvider.CurrentUser;
        conn.Update(existing);

        _checked.TryAdd(key, 0);
    }

    private static (byte[] hash, string json) Fingerprint(CapturedColumn[] columns)
    {
        var sb = new StringBuilder();
        foreach (var c in columns) sb.Append(c.Name).Append(':').Append(c.ClrType).Append('\n');
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var json = JsonSerializer.Serialize(columns);
        return (hash, json);
    }

    private static IEnumerable<string> ExtractNames(string json)
    {
        CapturedColumn[]? parsed = null;
        try { parsed = JsonSerializer.Deserialize<CapturedColumn[]>(json); }
        catch { /* malformed sidecar JSON — treat as "no prior names" */ }
        if (parsed is null) yield break;
        foreach (var c in parsed) yield return c.Name;
    }

    private static bool IsUniqueOrDuplicate(Exception ex)
    {
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            var msg = cur.Message ?? "";
            if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Cannot insert duplicate key", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Violation of UNIQUE", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Violation of PRIMARY KEY", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
