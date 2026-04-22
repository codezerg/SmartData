using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Contracts;
using SmartData.Server;
using SmartData.Server.SystemProcedures;
using SmartData.Server.Tracking;

namespace SmartData.Console.Controllers;

/// <summary>
/// Tracking & ledger read-only UI. Surfaces the system procedures shipped in
/// phases 3–5 under <c>/console/db/{db}/tables/{table}/tracking/*</c>:
/// History / Ledger / Verify / Digest / Schema-history. Backs onto
/// <c>sp_entity_history</c>, <c>sp_ledger_verify</c>, <c>sp_ledger_digest</c>,
/// <c>sp_schema_history</c>.
/// </summary>
public class TrackingController : ConsoleBaseController
{
    public TrackingController(IAuthenticatedProcedureService procedureService) : base(procedureService) { }

    [HttpGet("/console/db/{db}/tables/{table}/tracking")]
    [HttpGet("/console/db/{db}/tables/{table}/tracking/history")]
    public async Task<IActionResult> History(string db, string table, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        var model = await BuildPageAsync<HistoryListViewModel>(db, table, "history", ct);
        if (!model.HistoryExists)
        {
            await PopulateLayout(db, ct);
            return PageOrPartial("NotTracked", model);
        }

        var historyTable = $"{table}_History";
        var rows = await ExecuteAsync<List<Dictionary<string, object?>>>("sp_select",
            new { Database = db, Table = historyTable, Limit = limit, Offset = offset, OrderBy = "HistoryId:desc" }, ct);

        model.Rows = rows;
        model.Columns = rows.Count > 0 ? rows[0].Keys.ToList() : [];
        model.Offset = offset;
        model.Limit = limit;
        model.Total = await ResolveRowCountAsync(db, historyTable, ct);

        await PopulateLayout(db, ct);
        return PageOrPartial("History", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/tracking/ledger")]
    public async Task<IActionResult> Ledger(string db, string table, int limit = 50, int offset = 0, CancellationToken ct = default)
    {
        var model = await BuildPageAsync<LedgerListViewModel>(db, table, "ledger", ct);
        if (!model.LedgerExists)
        {
            await PopulateLayout(db, ct);
            return PageOrPartial("NotTracked", model);
        }

        var ledgerTable = $"{table}_Ledger";
        var raw = await ExecuteAsync<List<Dictionary<string, object?>>>("sp_select",
            new { Database = db, Table = ledgerTable, Limit = limit, Offset = offset, OrderBy = "LedgerId:desc" }, ct);

        model.Rows = raw.Select(MapLedgerRow).ToList();
        model.Total = await ResolveRowCountAsync(db, ledgerTable, ct);
        model.Offset = offset;
        model.Limit = limit;

        await PopulateLayout(db, ct);
        return PageOrPartial("Ledger", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/tracking/verify")]
    public async Task<IActionResult> Verify(string db, string table, CancellationToken ct = default)
    {
        var model = await BuildPageAsync<VerifyViewModel>(db, table, "verify", ct);
        if (!model.LedgerExists)
        {
            await PopulateLayout(db, ct);
            return PageOrPartial("NotTracked", model);
        }

        model.Result = await ExecuteAsync<VerificationResult>("sp_ledger_verify",
            new { Database = db, Table = table }, ct);

        await PopulateLayout(db, ct);
        return PageOrPartial("Verify", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/tracking/digest")]
    public async Task<IActionResult> Digest(string db, string table, CancellationToken ct = default)
    {
        var mode = await ResolveModeAsync(db, table, ct);
        if (mode.LedgerExists == false)
            return NotFound($"'{table}' has no ledger in '{db}'.");

        var digest = await ExecuteAsync<LedgerDigest>("sp_ledger_digest",
            new { Database = db, Table = table }, ct);

        // JSONL line: spec § Digests & Anchoring — LatestRowHash hex-encoded,
        // newline-terminated, pipe into >> digest archive.
        var dto = new
        {
            digest.TableName,
            digest.LatestLedgerId,
            LatestRowHash = Convert.ToHexString(digest.LatestRowHash),
            ChangedOn = digest.ChangedOn.ToString("o"),
            digest.EntryCount,
        };
        var line = JsonSerializer.Serialize(dto) + "\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(line);
        return File(bytes, "application/json-lines", $"{table}-digest-{DateTime.UtcNow:yyyyMMddHHmmss}.jsonl");
    }

    [HttpGet("/console/db/{db}/tables/{table}/tracking/schema")]
    public async Task<IActionResult> Schema(string db, string table, CancellationToken ct = default)
    {
        var model = await BuildPageAsync<SchemaHistoryViewModel>(db, table, "schema", ct);
        if (!model.HistoryExists && !model.LedgerExists)
        {
            await PopulateLayout(db, ct);
            return PageOrPartial("NotTracked", model);
        }

        model.Timeline = await ExecuteAsync<SchemaHistoryResult>("sp_schema_history",
            new { Database = db, Table = table }, ct);

        await PopulateLayout(db, ct);
        return PageOrPartial("Schema", model);
    }

    // ---- helpers --------------------------------------------------------

    private async Task<T> BuildPageAsync<T>(string db, string table, string activeTab, CancellationToken ct)
        where T : TrackingPageViewModel, new()
    {
        var resolved = await ResolveModeAsync(db, table, ct);
        return new T
        {
            Db = db,
            Table = table,
            ActiveTab = activeTab,
            Mode = resolved.Mode,
            HistoryExists = resolved.HistoryExists,
            LedgerExists = resolved.LedgerExists,
        };
    }

    private async Task<(string Mode, bool HistoryExists, bool LedgerExists)> ResolveModeAsync(
        string db, string table, CancellationToken ct)
    {
        var tables = await ExecuteAsync<List<TableListItem>>("sp_table_list",
            new { Database = db }, ct);
        var names = tables.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var historyExists = names.Contains($"{table}_History");
        var ledgerExists = names.Contains($"{table}_Ledger");
        var mode = ledgerExists ? "ledger" : historyExists ? "tracked" : "none";
        return (mode, historyExists, ledgerExists);
    }

    private async Task<long> ResolveRowCountAsync(string db, string table, CancellationToken ct)
    {
        var tables = await ExecuteAsync<List<TableListItem>>("sp_table_list",
            new { Database = db }, ct);
        return tables.FirstOrDefault(t => string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase))?.RowCount ?? 0;
    }

    private static LedgerRowView MapLedgerRow(Dictionary<string, object?> row)
    {
        var historyId = row.TryGetValue("HistoryId", out var hid) && hid is not null ? Convert.ToInt64(hid) : (long?)null;
        var prev = row.TryGetValue("PrevHash", out var p) ? p as byte[] ?? [] : [];
        var hash = row.TryGetValue("RowHash", out var h) ? h as byte[] ?? [] : [];
        var cb = row.TryGetValue("CanonicalBytes", out var c) ? c as byte[] ?? [] : [];

        // Synthetic rows (HistoryId IS NULL) are either 'S' (schema) or 'P' (prune);
        // distinguishing them requires decoding CanonicalBytes. For the list view
        // we label as "Synthetic" — the per-row decode is deferred to future detail
        // views (spec § Admin Console — Entity → Ledger).
        var kind = historyId is null ? "Synthetic" : "Entity";
        var op = historyId is null ? "S/P" : ""; // resolved per-row in future detail view

        return new LedgerRowView
        {
            LedgerId = Convert.ToInt64(row["LedgerId"]),
            HistoryId = historyId,
            Kind = kind,
            OperationLabel = op,
            ChangedOn = DateTime.MinValue, // requires decoding; deferred
            ChangedBy = "",
            PrevHashHex = Convert.ToHexString(prev, 0, Math.Min(4, prev.Length)),
            RowHashHex = Convert.ToHexString(hash, 0, Math.Min(4, hash.Length)),
            CanonicalBytesLength = cb.Length,
        };
    }
}
