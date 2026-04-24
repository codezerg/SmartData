---
title: Entities & AutoRepo
description: Plain classes map to tables. SmartData handles schema migration, indexes, and full-text search.
---

Entities are plain C# classes decorated with attributes. At first use, SmartData compares the class to the database schema and brings the database into alignment — creating missing tables, adding missing columns. This is **AutoRepo**.

## A minimal entity

```csharp
using LinqToDB.Mapping;

namespace MyApp.Entities;

[Table]
public class Product
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }

    [Column] public string Name { get; set; } = "";
    [Column] public decimal Price { get; set; }
    [Column, Nullable] public string? Description { get; set; }
    [Column] public bool IsActive { get; set; } = true;
}
```

The `[Table]`, `[Column]`, `[PrimaryKey]`, `[Identity]`, and `[Nullable]` attributes come from [LinqToDB](https://linq2db.github.io/) — SmartData uses LinqToDB as the underlying ORM. Anything LinqToDB understands on a mapping attribute works here.

## Attribute cheatsheet

| Attribute | Target | Purpose |
| --- | --- | --- |
| `[Table]` | Class | Maps the class to a table (table name = class name) |
| `[Column]` | Property | Maps the property to a column |
| `[PrimaryKey, Identity]` | Property | Primary key with auto-increment |
| `[Nullable]` | Property | Nullable column |
| `[MaxLength(n)]` | Property | Column max length (e.g. `VARCHAR(256)`) |
| `[Index("name", ...)]` | Class (stackable) | Non-clustered index (SmartData) |
| `[FullTextIndex(...)]` | Class | Full-text index (SmartData) |

`[Index]` and `[FullTextIndex]` are SmartData's own — they live in `SmartData.Server.Attributes`.

## Indexes

```csharp
using SmartData.Server.Attributes;

[Table]
[Index("IX_Customer_Status", nameof(Status))]
[Index("IX_Customer_Email", nameof(ContactEmail), Unique = true)]
[FullTextIndex(nameof(CompanyName), nameof(ContactName), nameof(Notes))]
public class Customer { /* ... */ }
```

- `[Index]` applies to the class (not the property) so composite indexes are natural.
- Multiple `[Index]` attributes stack.
- Index names get a configurable prefix (default `SD_`) in the database so they're easy to spot.

Query full-text indexes with `ctx.FullTextSearch<T>(term)` from inside a procedure (see [Database context](/fundamentals/database-context/)).

## Audit field convention

Most entities should carry four audit fields:

```csharp
[Column] public DateTime CreatedOn { get; set; }
[Column] public string   CreatedBy { get; set; } = "";
[Column, Nullable] public DateTime? ModifiedOn { get; set; }
[Column, Nullable] public string?   ModifiedBy { get; set; }
```

Nothing in the framework enforces these — procedures set them. The pattern: declare a `public string CurrentUser { get; set; } = "";` parameter on save/delete procedures; the caller passes the acting user's identity; the procedure writes it. This keeps procedures decoupled from ASP.NET's authentication plumbing.

## Schema modes

```csharp
builder.Services.AddSmartData(options =>
{
    options.SchemaMode = SchemaMode.Auto;     // default
    // options.SchemaMode = SchemaMode.Manual;
});
```

| Mode | Behaviour |
| --- | --- |
| `SchemaMode.Auto` | On first use of each entity, the class is compared to the database. Missing tables, columns, and indexes are created. |
| `SchemaMode.Manual` | No automatic changes. Your class must match the database exactly. |

### What Auto does NOT do

Auto migration is deliberately narrow:

- **Never drops columns.** Renaming a property creates a new column; the old one stays with its data.
- **Never renames columns.** Same reason — rename = new + old untouched.
- **Never migrates data.** Column type changes are not retyped; adds and leaves.
- **Does not drop tables.** Removing an entity class leaves the table behind.

In short: Auto is additive. Destructive changes require manual SQL or a migration tool of your own choosing.

> **Production caution.** `Auto` is great for local dev and preview environments. For production, either keep `Auto` **and** commit to additive-only schema changes, or flip to `Manual` and apply schema changes yourself. Renaming-then-deploying under `Auto` silently accumulates orphan columns.

## Property conventions

- Non-nullable strings → default to `""` (`public string Name { get; set; } = "";`). Avoids nullable-reference noise and null coming back from the database.
- Nullable → `?` plus `[Nullable]` (`[Column, Nullable] public string? Notes { get; set; }`).
- Relationships → foreign-key properties (`CustomerId`), not navigation properties. Joins happen in procedures where you can keep them explicit.
- No inheritance. Entities are flat classes.

## Related

- [Database context](/fundamentals/database-context/) — `ctx.GetTable<T>()`, inserts, updates, transactions
- [Add a new entity](/how-to/add-a-new-entity/) — step-by-step recipe
- [Enable change tracking](/how-to/enable-change-tracking/) — the `[Tracked]`/`[Ledger]` opt-ins
- [Tracking & Ledger](/fundamentals/tracking/) — the audit-history system
