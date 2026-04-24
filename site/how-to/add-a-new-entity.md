---
title: Add a new entity
description: Define an entity class — AutoRepo picks it up on first use.
---

Create a class with `[Table]` and `[Column]` attributes. No registration, no migration file.

```csharp
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

namespace MyApp.Entities;

[Table]
[Index("IX_Product_Sku", nameof(Sku), Unique = true)]
[Index("IX_Product_Category", nameof(CategoryId))]
public class Product
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }

    [Column, MaxLength(64)] public string Sku   { get; set; } = "";
    [Column]                public string Name  { get; set; } = "";
    [Column]                public decimal Price { get; set; }
    [Column]                public int    CategoryId { get; set; }
    [Column, Nullable]      public string? Description { get; set; }

    // Audit fields
    [Column] public DateTime CreatedOn { get; set; }
    [Column] public string   CreatedBy { get; set; } = "";
    [Column, Nullable] public DateTime? ModifiedOn { get; set; }
    [Column, Nullable] public string?   ModifiedBy { get; set; }
}
```

## What happens on first use

With `SchemaMode.Auto` (default), the first time a procedure does `ctx.GetTable<Product>()` or `ctx.Insert(new Product { ... })`:

1. SmartData inspects the database.
2. If the `Product` table is missing, it's created with all columns.
3. Missing columns are added.
4. Missing indexes are created.

Subsequent calls skip the check.

## Conventions to follow

- Initialize non-nullable strings to `""` so they never come back null.
- Nullable columns → `[Nullable]` on the column + `?` on the type.
- Relationships → foreign-key properties (`CategoryId`), not navigation properties.
- No inheritance; entities are flat.
- Audit fields (`CreatedOn/By`, `ModifiedOn/By`) aren't required but conventional — procedures set them.

## What Auto will NOT do for you

- Rename a property → you get a new column; the old one stays with its data.
- Change a column's type → not migrated.
- Delete a removed property → column stays.
- Drop the table if you delete the class → stays.

If you need a destructive change, do it yourself in a migration.

## Related

- [Entities & AutoRepo](/fundamentals/entities/) — the full mental model
- [Define a procedure](/how-to/define-a-procedure/) — read/write the entity in procedures
- [Enable change tracking](/how-to/enable-change-tracking/) — add `[Tracked]` / `[Ledger]`
