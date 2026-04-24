---
title: Migrate an existing schema
description: Bring an existing database under SmartData without breaking it.
---

You have a database. Tables, indexes, data, probably some of it load-bearing. You want to put SmartData in front of it without AutoRepo "helpfully" rewriting anything on first call. About 20 minutes end-to-end.

The short version:

1. Wire SmartData with `SchemaMode.Manual`.
2. Write entity classes that match existing tables exactly.
3. Introduce procedures one subsystem at a time.
4. Flip to `SchemaMode.Auto` only once entities and DB are fully in sync.

## 1. Register with SchemaMode.Manual

```csharp
using SmartData;                     // AddSmartData, AddStoredProcedures, UseSmartData
using SmartData.Server.Providers;    // SchemaMode
using SmartData.Server.Sqlite;       // AddSmartDataSqlite

builder.Services.AddSmartData(o => o.SchemaMode = SchemaMode.Manual);
builder.Services.AddSmartDataSqlite(o => o.DataDirectory = "/var/app/db");
builder.Services.AddStoredProcedures(typeof(Program).Assembly);

var app = builder.Build();
app.UseSmartData();
```

In `Manual` mode AutoRepo never issues DDL on its own. System procedures (`sp_table_create`, `sp_column_add`, etc.) still work for explicit operations. See [Entities → Schema modes](/fundamentals/entities/) for the full behavior contract.

If your existing DB lives in a shared directory, point `DataDirectory` at it and name your entity's `[Table]` to match the file/table that's already there. If you're on SQL Server, use `AddSmartDataSqlServer` — the provider appends `Initial Catalog={dbName}` per request, so point `ConnectionString` at the server only.

## 2. Map an entity to an existing table

Say your legacy schema has:

```sql
CREATE TABLE dbo.Customers (
    customer_id    INT IDENTITY PRIMARY KEY,
    company_name   NVARCHAR(200) NOT NULL,
    contact_email  NVARCHAR(200) NULL,
    is_active      BIT NOT NULL DEFAULT 1,
    created_on     DATETIME2 NOT NULL
);
CREATE INDEX IX_Customers_Email ON dbo.Customers(contact_email);
```

The entity that lines up byte-for-byte:

```csharp
using LinqToDB.Mapping;

namespace MyApp.Entities;

[Table(Name = "Customers", Schema = "dbo")]
public class Customer
{
    [PrimaryKey, Identity]
    [Column(Name = "customer_id")] public int     Id           { get; set; }
    [Column(Name = "company_name")] public string CompanyName  { get; set; } = "";
    [Column(Name = "contact_email"), Nullable] public string? ContactEmail { get; set; }
    [Column(Name = "is_active")]   public bool     IsActive     { get; set; }
    [Column(Name = "created_on")]  public DateTime CreatedOn    { get; set; }
}
```

Points that bite:

- **Name mismatches** — `[Column(Name = "...")]` is how you decouple the CLR property from the DB column. Don't rename and expect the framework to figure it out.
- **Nullability** — `string?` alone isn't enough; pair it with `[Nullable]` on reference types that map to nullable columns. Mismatches are what trigger destructive "migrations" if you later flip to `Auto`.
- **Don't add `[Index]`** — the index already exists. `[Index]` adds SmartData-managed, prefixed indexes; applying one to an existing hand-rolled index will leave the DB with both.
- **Identity columns** — `[PrimaryKey, Identity]` works against both `IDENTITY` (SQL Server) and `AUTOINCREMENT INTEGER PRIMARY KEY` (SQLite).

## 3. Verify the mapping before writing procedures

The cheapest read-only check:

```csharp
public class CustomerCount : StoredProcedure<CountResult>
{
    public CustomerCount(IDatabaseContext ctx) { }

    public override CountResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var count = ctx.GetTable<Customer>().Count();
        var first = ctx.GetTable<Customer>().Take(1).ToList();
        return new CountResult { Count = count, Sample = first };
    }
}
```

If this returns the right numbers on prod-like data, your mapping is correct. If a column name is wrong, linq2db throws on the first read, not later. Fix the mapping; don't reach for DDL.

You can also call `sp_table_describe` for a sanity check against what SmartData *thinks* the table looks like vs. your entity — handy when diagnosing index drift.

## 4. Introduce procedures one subsystem at a time

Resist the urge to port everything at once. A safer rhythm:

1. Pick one read path (`CustomerList`, `CustomerGet`). Map the entity. Ship it behind the existing API.
2. Add writes (`CustomerSave`, `CustomerDelete`) once reads are stable.
3. Move to the next aggregate.

Each procedure gets its own DTO folder under `Contracts/` — keep legacy call sites decoupled from internal entity shape. See [Return DTOs, not entities](/how-to/return-dtos-not-entities/).

Audit columns (`CreatedOn` / `CreatedBy` / `ModifiedOn` / `ModifiedBy`) aren't populated by the framework. Pass a `CurrentUser` parameter from the caller and set them in the procedure. Details in [Procedures → Audit fields](/fundamentals/procedures/).

## 5. Handling the `_sys_*` tables

SmartData creates `_sys_users`, `_sys_logs`, `_sys_settings`, etc. in the master database on first startup. If your existing DB *is* master, those tables will appear alongside your own — they start with `_sys_` specifically so `sp_table_list` and the admin console filter them out. If that's unacceptable, run SmartData against a dedicated master database and keep your app data in a separate one. You can `db.UseDatabase("legacy")` inside procedures to switch targets — see [Multi-database access](/reference/smartdata-server/#idatabasecontext).

## 6. When to flip to `SchemaMode.Auto`

Only when:

- Every entity used by a registered procedure has a matching DB table, verified by readback.
- Every entity property has a `[Column]` with the right name/nullability.
- You've reviewed the Auto-mode contract: it adds tables/columns/indexes and **never drops or renames columns or migrates data**. Renaming a property creates a new column next to the old one. See [Entities → Schema modes](/fundamentals/entities/).

The practical rule: keep Manual in production unless you *want* those semantics. Auto is fine in dev.

## 7. Gotchas worth knowing upfront

- **Case-sensitive engines.** linq2db quotes identifiers; if your DB is case-sensitive, mismatched `[Column(Name = "...")]` casing produces a "no such column" error, not a warning.
- **Default values.** Adding a new column via `sp_column_add` with `Nullable: false` requires a default for existing rows; the provider handles standard primitive defaults (0, empty string, false). Use `sp_column_add` explicitly rather than relying on Auto for this — it's more controllable.
- **Index prefix.** `IndexOptions.Prefix` defaults to `"SD_"` and is what makes auto-drop safe — SmartData only drops indexes it sees its own prefix on. If you *must* manage an existing index via `[Index]`, give it a name with the prefix and be ready for SmartData to drop + recreate it on changes.
- **Full-text indexes** are provider-specific. SQLite uses FTS5 shadow tables (`{table}_fts`); SQL Server uses a shared catalog. Review [SqliteSchemaOperations](/reference/smartdata-server-sqlite/#full-text-search-fts5) or [SqlServerSchemaOperations](/reference/smartdata-server-sqlserver/#full-text-search) before adding `[FullTextIndex]` to an existing table.

## Where to go next

- [Fundamentals → Entities](/fundamentals/entities/) — attribute reference + Auto mode's exact contract.
- [How-to → Write a custom provider](/how-to/write-a-custom-provider/) — when neither bundled provider fits.
- [Reference → System procedures](/reference/system-procedures/) — explicit DDL procedures for when you don't want AutoRepo making decisions.
