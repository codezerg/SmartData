---
title: Query entity history
description: Read change history and verify ledger integrity.
---

Two ways to read captured history: hit the history surface directly from a procedure, or go through the system procedure for a normalised view that works from any caller.

## Direct access from a procedure

Every `[Tracked]` entity has a backing `{Table}_History` table, but you don't query it as a custom type — use `ctx.History<T>()`. It returns `IQueryable<HistoryEntity<T>>`, where `h.Data` flattens to the mirrored columns at SQL generation time.

```csharp
var rows = ctx.History<Product>()
    .Where(h => h.Data.Id == productId)
    .OrderByDescending(h => h.ChangedOn)
    .ToList();

foreach (var r in rows)
    Console.WriteLine($"{r.ChangedOn:u} {r.Operation} by {r.ChangedBy}");
```

Fields on `HistoryEntity<T>`: `HistoryId`, `Operation` (`"I"` / `"U"` / `"D"`), `ChangedOn`, `ChangedBy`, `Data` (a populated `T` for the post-image — or last-known state on delete).

## Via `sp_entity_history`

A normalised read that works for any tracked table without the generic type — useful from clients or admin tooling. Parameters: `Database`, `Table`, `Pk` (string form of the source PK — single-column only in v1), `Limit` (default 100), `Offset` (default 0).

```csharp
var result = await ctx.ExecuteAsync<SpEntityHistory.Result>(
    "sp_entity_history",
    new
    {
        Database = "master",
        Table    = "Product",
        Pk       = productId.ToString(),
        Limit    = 50,
        Offset   = 0,
    });

foreach (var entry in result.Items)
    Console.WriteLine($"#{entry.HistoryId} {entry.Operation} {entry.ChangedOn:u} by {entry.ChangedBy}");
```

`Entry.State` is a `Dictionary<string, object?>` with the snapshot columns.

## Verifying a ledger

`sp_ledger_verify` returns a `VerificationResult`. Internal consistency is always populated; anchor fields populate only when `Anchors` are supplied.

```csharp
var verify = await ctx.ExecuteAsync<VerificationResult>(
    "sp_ledger_verify",
    new
    {
        Database = "master",
        Table    = "Invoice",
    });

if (!verify.InternalConsistencyValid)
    throw new InvalidOperationException(
        $"Ledger tamper at LedgerId {verify.FirstBadLedgerId} ({verify.FailureKind}).");
```

Verification recomputes each row's hash from its payload + previous hash and compares to the stored hash. A mismatch means either the payload or the chain was altered. Run periodically, and on-demand before high-stakes reads, on ledgered tables.

## External anchoring

For verification to be a meaningful integrity check, you need an **external anchor** — a digest you commit outside the database so an attacker can't rewrite both the chain and the anchor.

```csharp
var digest = await ctx.ExecuteAsync<LedgerDigest>(
    "sp_ledger_digest",
    new { Database = "master", Table = "Invoice" });

// Commit digest.LatestLedgerId, digest.LatestRowHash, digest.ChangedOn
// to your anchor store (object storage, secondary DB, printed report, etc.).
```

Feed captured digests back to `sp_ledger_verify` via the `Anchors` parameter to confirm nothing in the prior range changed.

```csharp
var verify = await ctx.ExecuteAsync<VerificationResult>(
    "sp_ledger_verify",
    new { Database = "master", Table = "Invoice", Anchors = storedDigests });
```

## Pruning

Over time, `_History` and `_Ledger` tables grow. Prune to a cutoff using the `OlderThan` UTC timestamp:

```csharp
// History: safe to prune at any cutoff
await ctx.ExecuteAsync<SpHistoryPrune.Result>("sp_history_prune",
    new { Database = "master", Table = "Product", OlderThan = DateTime.UtcNow.AddYears(-1) });

// Ledger: prunes the history + ledger together inside a transaction and chains a
// synthetic 'P' marker. Anchor the surviving head externally first if you need to
// preserve tamper-evidence for the pruned range.
await ctx.ExecuteAsync<SpLedgerPrune.Result>("sp_ledger_prune",
    new { Database = "master", Table = "Invoice", OlderThan = DateTime.UtcNow.AddYears(-7) });
```

`sp_history_prune` is refused on ledgered tables — pair with `sp_ledger_prune` there.

## Related

- [Tracking & Ledger](/fundamentals/tracking/) — what's captured, integrity guarantees
- [Enable change tracking](/how-to/enable-change-tracking/) — the opt-in
- [System procedures → Tracking](/reference/system-procedures/) — the full `sp_*` list
