---
title: Query entity history
description: Read change history and verify ledger integrity.
---

Two ways to read captured history: hit the generated table directly, or go through the system procedure for a normalised view.

## Direct table access

Every `[Tracked]` entity gets a `{Table}_History` companion. Query it like any other entity:

```csharp
var rows = ctx.GetTable<ProductHistory>()
    .Where(h => h.EntityId == productId)
    .OrderByDescending(h => h.ChangedOn)
    .ToList();

foreach (var r in rows)
    Console.WriteLine($"{r.ChangedOn:u} {r.Operation} by {r.ChangedBy}");
```

Fields you get on every history row: `Id`, `EntityId`, `Operation` (`Insert`/`Update`/`Delete`), `ChangedOn`, `ChangedBy`, plus a snapshot of the tracked columns.

## Via `sp_entity_history`

A normalised read that works for any tracked table without needing the generated type:

```csharp
var result = await ctx.ExecuteAsync<EntityHistoryResult>(
    "sp_entity_history",
    new
    {
        Table    = "Product",
        EntityId = productId,
        From     = DateTime.UtcNow.AddDays(-30),
        To       = (DateTime?)null,
        Page     = 1,
        PageSize = 50,
    });
```

## Verifying a ledger

```csharp
var verify = await ctx.ExecuteAsync<LedgerVerifyResult>(
    "sp_ledger_verify",
    new
    {
        Table = "Invoice",
        From  = DateTime.UtcNow.AddDays(-90),
    });

if (!verify.Ok)
    throw new InvalidOperationException($"Ledger tamper detected at row {verify.FirstBrokenRowId}");
```

Verification recomputes each row's hash from its payload + previous hash and compares to the stored hash. A mismatch means either the payload or the chain was altered. Run it periodically (and on-demand before high-stakes reads) on ledgered tables.

## External anchoring

For verification to be a meaningful integrity check, you need an **external anchor** — a hash you commit outside the database so an attacker can't rewrite both the chain and the anchor.

```csharp
var digest = await ctx.ExecuteAsync<LedgerDigestResult>(
    "sp_ledger_digest",
    new { Table = "Invoice" });

// Commit digest.Hash and digest.RowId to your anchor store
// (object storage, secondary DB, blockchain, printed report, whatever).
```

Compare anchored digests against a fresh `sp_ledger_digest` result to confirm nothing in the prior range changed.

## Pruning

Over time, `_History` and `_Ledger` tables grow. Prune to a cutoff:

```csharp
// History: safe to prune at any cutoff
await ctx.ExecuteAsync<VoidResult>("sp_history_prune",
    new { Table = "Product", Before = DateTime.UtcNow.AddYears(-1) });

// Ledger: pruning breaks the chain before the cutoff. Anchor the surviving
// head externally first; otherwise you lose integrity for the pruned range.
await ctx.ExecuteAsync<VoidResult>("sp_ledger_prune",
    new { Table = "Invoice", Before = DateTime.UtcNow.AddYears(-7) });
```

## Related

- [Tracking & Ledger](/fundamentals/tracking/) — what's captured, integrity guarantees
- [Enable change tracking](/how-to/enable-change-tracking/) — the opt-in
- [System procedures → Tracking](/reference/system-procedures/) — the full `sp_*` list
