---
title: Tracking & Ledger
description: Opt-in entity history — queryable mirror, optional tamper-evident chain.
---

Two opt-in features for capturing entity mutation history, applied at the class level:

- **`[Tracked]`** — queryable history. Every `INSERT` / `UPDATE` / `DELETE` routed through `IDatabaseContext` writes a mirror row to `{Table}_History` with operation, timestamp, and the acting user.
- **`[Ledger]`** — queryable history *plus* a tamper-evident chain. Writes to both `{Table}_History` and `{Table}_Ledger`. The ledger table chains SHA-256 hashes, so tampering with past rows is detectable given external anchoring.

`[Ledger]` implies `[Tracked]`. The two tables are independent on the wire: query `_History` for ergonomics (joins, projections, LINQ); query *and verify* `_Ledger` for integrity. There is no foreign key between them.

## Minimal opt-in

```csharp
using SmartData.Server.Attributes;

[Table]
[Ledger]   // implies [Tracked]; drop to [Tracked] for history-only
public class Invoice
{
    [PrimaryKey, Identity, Column] public int Id { get; set; }
    [Column] public string Customer { get; set; } = "";
    [Column] public decimal Amount { get; set; }
    [Column, NotTracked] public string? InternalNotes { get; set; }  // excluded from history + ledger
}
```

Writes go through the normal entity APIs — no new call site. `ctx.Insert(invoice)` produces one `Invoice_History` row and, for ledgered entities, one paired `Invoice_Ledger` row hashed onto the chain.

## Attributes

| Attribute | Target | Effect |
| --- | --- | --- |
| `[Tracked]` | Class | Mirror writes to `{Table}_History` |
| `[Ledger]` | Class | `[Tracked]` + hash-chained `{Table}_Ledger` |
| `[NotTracked]` | Property | Exclude the column from both history and ledger |

## How a write becomes a history row

1. Procedure calls `ctx.Insert/Update/Delete` on a `[Tracked]` (or `[Ledger]`) entity.
2. The tracking write-path captures the old and new row (as applicable), resolves the acting user from the context, and writes the history row in the same transaction as the data row.
3. For `[Ledger]`, a ledger row is also written — payload + previous hash + new hash. A `UNIQUE(PrevHash)` index means concurrent writers retry rather than forking the chain.

Failures in the history write fail the data write too: there is no silent drop. If your procedure observes `Insert` returning successfully, the history/ledger side-effect committed.

## Reading history

From any procedure, go direct to the generated table:

```csharp
var history = ctx.GetTable<InvoiceHistory>()
    .Where(h => h.EntityId == invoiceId)
    .OrderByDescending(h => h.ChangedOn)
    .ToList();
```

Or call the system procedure for a normalised view:

```csharp
var changes = await ctx.ExecuteAsync<EntityHistoryResult>(
    "sp_entity_history",
    new { Table = "Invoice", EntityId = invoiceId });
```

See [Query entity history](/how-to/query-entity-history/) for the full surface.

## Verifying the ledger

Integrity verification compares the stored hash of each row to a recomputed hash from its payload + previous hash. An external anchor (pin a digest outside the database) is required for the guarantee to be meaningful — without it, an attacker can rewrite past rows *and* the chain.

```csharp
await ctx.ExecuteAsync<LedgerVerifyResult>("sp_ledger_verify",
    new { Table = "Invoice", From = DateTime.UtcNow.AddDays(-30) });
```

Digests (a rolling hash you commit externally) come from `sp_ledger_digest`. Anchor frequency is a trade-off: more frequent = smaller lost window if an anchor is missed.

## Column drift (`[Tracked]` only)

Renaming or removing columns is handled defensively. A sidecar table (`_sys_tracked_columns`) records the shape captured at first use. When AutoRepo adds a column or you remove one, the sidecar notices and logs a drift warning. The history table stays compatible: new columns get backfilled `null`s in old rows; removed columns keep their past data.

For `[Ledger]` the story is stricter — because a ledger's payload *is* the recorded columns, changing the column set means branching the chain. The writer detects this and starts a new baseline rather than silently continuing.

## What this feature is not

- **Not point-in-time restore.** History lets you *answer* "what did this row look like at T?". It does not rewind the live table. That's what backups are for.
- **Not audit for every service call.** It captures *data mutations*. Reads, denied-auth attempts, and non-entity events are out of scope — use your app's logging for those.
- **Not free.** Every `Insert` / `Update` / `Delete` on a `[Tracked]` entity writes at least one extra row; `[Ledger]` writes two. Hot-path entities should stay plain unless you actually need the history.

## Related

- [Enable change tracking](/how-to/enable-change-tracking/) — decorate an entity, first write, first query
- [Query entity history](/how-to/query-entity-history/) — read via `sp_entity_history`, verify via `sp_ledger_verify`
- [System procedures → Tracking](/reference/system-procedures/) — `sp_entity_history`, `sp_ledger_digest`, `sp_ledger_verify`, `sp_history_prune`, `sp_ledger_prune`, `sp_tracking_drop`, `sp_ledger_drop`
- Long-form internals: `docs/SmartData.Server.Tracking.md` in the repo
