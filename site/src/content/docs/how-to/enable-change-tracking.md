---
title: Enable change tracking
description: Turn on history or ledger on an entity.
---

Add `[Tracked]` for queryable history, `[Ledger]` for tamper-evident history.

```csharp
using SmartData.Server.Attributes;
using LinqToDB.Mapping;

[Table]
[Tracked]                  // queryable history only
public class Product { /* ... */ }

[Table]
[Ledger]                   // history + hash chain (implies Tracked)
public class Invoice { /* ... */ }
```

That's the whole opt-in. On next startup, AutoRepo creates:

- **`[Tracked]`** → `Product_History` table + `_sys_tracked_columns` sidecar
- **`[Ledger]`** → `Invoice_History` + `Invoice_Ledger` + sidecar

From then on, every `ctx.Insert` / `ctx.Update` / `ctx.Delete` on the entity writes a history row (and, for ledger, a chain row) in the same transaction as the data row.

## Exclude columns

Use `[NotTracked]` on properties that shouldn't be captured:

```csharp
[Table]
[Tracked]
public class User
{
    [PrimaryKey, Identity, Column] public int Id { get; set; }
    [Column] public string Email { get; set; } = "";

    [Column, NotTracked] public string PasswordHash { get; set; } = "";  // not in history
    [Column, NotTracked] public string? SessionToken { get; set; }
}
```

## No procedure changes needed

Writes go through the same entity APIs. Your `CustomerSave` procedure looks the same whether `Customer` is tracked or not. The capture is automatic.

## Cost

Every tracked write has overhead:
- `[Tracked]` → one extra row per write.
- `[Ledger]` → two extra rows per write (`_History` + `_Ledger`), plus hash compute.

Reserve for entities where the audit trail pays for itself. Hot-path entities (events, metrics, sessions) should stay plain.

## Verifying it's working

```csharp
// Insert a row
var p = ctx.Insert(new Product { Name = "Widget", Price = 9.99m, CreatedBy = CurrentUser });

// History mirror appeared — read via ctx.History<T>(), not a generated companion type.
// Rows are HistoryEntity<Product> — the source entity sits on h.Data.
var firstHistory = ctx.History<Product>()
    .First(h => h.Data.Id == p.Id);
// h.Operation == "I" (I/U/D), h.ChangedBy == CurrentUser, h.ChangedOn ≈ now, h.Data == snapshot
```

## Turning it off

Remove the attribute. Restart. Writes stop being captured. Existing history is preserved; nothing is dropped automatically. Use `sp_tracking_drop` / `sp_ledger_drop` if you want to remove the backing tables.

## Related

- [Tracking & Ledger](/fundamentals/tracking/) — mental model, verification, what this is not
- [Query entity history](/how-to/query-entity-history/) — how to read the captures
- [System procedures → Tracking](/reference/system-procedures/) — `sp_entity_history`, `sp_ledger_verify`
