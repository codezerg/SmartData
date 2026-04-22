# SmartData Developer Guide

A practical guide to building applications with the SmartData framework. Covers project setup, defining entities, writing stored procedures, creating contracts, and calling procedures from any .NET application.

---

## Table of Contents

1. [Overview](#overview)
2. [Project Setup](#project-setup)
3. [Entities](#entities)
4. [Contracts](#contracts)
5. [Stored Procedures](#stored-procedures)
6. [IDatabaseContext API](#idatabasecontext-api)
7. [Common Patterns](#common-patterns)
8. [Calling Procedures](#calling-procedures)
9. [Scheduling](#scheduling)
10. [RPC Protocol](#rpc-protocol)
11. [Production Considerations](#production-considerations)
12. [What This Guide Doesn't Cover](#what-this-guide-doesnt-cover)

---

## Overview

SmartData is a .NET framework for building data-driven applications. It provides:

- **AutoRepo ORM** — Define entities with attributes. SmartData creates tables, adds columns, and manages indexes automatically.
- **Stored Procedure Framework** — All business logic lives in typed procedure classes. No controllers, no service layers — just procedures. The name "stored procedure" is borrowed from SQL databases, but these are C# classes — there is no T-SQL. The analogy: like a database stored procedure, each one is a named, parameterized unit of work that the framework discovers, registers, and executes by name.
- **Binary RPC** — A single `POST /rpc` endpoint handles all communication. Requests and responses use a compact binary format.
- **Built-in Infrastructure** — Session management, metrics, backups, background execution, scheduling, and health checks out of the box.

### Architecture

```
Your Application
├── Entities/        Entity classes → database tables (auto-migrated)
├── Procedures/      StoredProcedure<T> classes → business logic
├── Contracts/       DTOs shared between server and client
└── Program.cs       Service registration + middleware

SmartData Framework
├── SmartData.Server          Engine, ORM, procedure executor, session, metrics
├── SmartData.Server.Sqlite   SQLite database provider
├── SmartData.Server.SqlServer SQL Server database provider
├── SmartData.Client           HTTP client for remote RPC calls
└── SmartData.Core             Binary serialization protocol
```

### How It Works

1. You define **entities** (C# classes with attributes) — SmartData creates the database schema.
2. You write **procedures** (classes that extend `StoredProcedure<T>`) — SmartData discovers and registers them.
3. You define **contracts** (plain DTO classes) — shared between procedure return types and callers.
4. Callers invoke procedures either **directly** (inject `IProcedureService`) or **remotely** (`POST /rpc`).

---

## Project Setup

### Minimal Program.cs

```csharp
using SmartData;

var builder = WebApplication.CreateBuilder(args);

// 1. Register SmartData core services
builder.Services.AddSmartData();

// 2. Register a database provider (pick one)
builder.Services.AddSmartDataSqlite();
// builder.Services.AddSmartDataSqlServer(o =>
// {
//     o.ConnectionString = "Server=...;Database=...;Trusted_Connection=true";
// });

// 3. Register your stored procedures (scans the assembly)
builder.Services.AddStoredProcedures(typeof(Program).Assembly);

// 4. (Optional) Enable the scheduler — lets any [Daily]/[Every]/... procedure fire on a timer
// builder.Services.AddSmartDataScheduler();

var app = builder.Build();

// 5. Map the /rpc endpoint and /health check
app.UseSmartData();

app.Run();
```

### What Each Call Does

| Method | Purpose |
|--------|---------|
| `AddSmartData()` | Registers the core engine: ORM, procedure executor, session manager, metrics, backup service, background queue, health checks |
| `AddSmartDataSqlite()` | Registers SQLite as the database provider. Databases stored in `data/{name}.db` |
| `AddSmartDataSqlServer(o => ...)` | Registers SQL Server as the database provider |
| `AddStoredProcedures(assembly)` | Scans the assembly for `IStoredProcedure` implementations and registers them in the catalog |
| `AddSmartDataScheduler(o => ...)` | Enables the scheduler: runs `ScheduleReconciler` at startup, then polls `sp_scheduler_tick` on a timer. Must be called **after** `AddStoredProcedures`. See [Scheduling](#scheduling). |
| `UseSmartData()` | Maps `POST /rpc` (binary RPC endpoint) and `GET /health` (health check) |

### Configuration

```csharp
builder.Services.AddSmartData(options =>
{
    // Schema migration mode (default: Auto)
    options.SchemaMode = SchemaMode.Auto;    // SmartData manages schema
    // options.SchemaMode = SchemaMode.Manual; // You manage schema

    // Error detail in responses (default: true in Development, false otherwise)
    options.IncludeExceptionDetails = true;

    // Metrics collection
    options.Metrics.Enabled = true;

    // Index management
    options.Index.AutoCreate = true;         // Auto-create indexes from [Index] attributes
    options.Index.AutoDrop = true;           // Auto-drop removed indexes
    options.Index.AutoCreateFullText = true;  // Auto-create full-text indexes
    options.Index.Prefix = "SD_";            // Prefix for managed index names
});
```

### Schema Modes

| Mode | Behavior |
|------|----------|
| `SchemaMode.Auto` | On first use of each entity, SmartData compares the class to the database — creates tables, adds missing columns, alters types. This is the default. |
| `SchemaMode.Manual` | No automatic migration. Your entity classes must match the database exactly. Useful when you manage schema externally. |

> **Production warning:** `SchemaMode.Auto` is convenient for development but requires caution in production. See [Production Considerations](#production-considerations) for details.

### Registering Individual Procedures

If you need to register a procedure with a custom name instead of the auto-generated one:

```csharp
builder.Services.AddStoredProcedure<MyProcedure>("custom_name");
```

---

## Entities

Entities are C# classes that map to database tables. SmartData uses [LinqToDB](https://linq2db.github.io/) attributes for mapping and its own attributes for indexes and full-text search.

### Basic Entity

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

    // Audit fields
    [Column] public DateTime CreatedOn { get; set; }
    [Column] public string CreatedBy { get; set; } = "";
    [Column, Nullable] public DateTime? ModifiedOn { get; set; }
    [Column, Nullable] public string? ModifiedBy { get; set; }
}
```

### Attributes Reference

| Attribute | Target | Purpose |
|-----------|--------|---------|
| `[Table]` | Class | Maps the class to a database table (table name = class name) |
| `[Column]` | Property | Maps the property to a column |
| `[PrimaryKey, Identity]` | Property | Marks the primary key with auto-increment |
| `[Nullable]` | Property | Marks the column as nullable in the database |
| `[MaxLength(n)]` | Property | Sets the column's max length (e.g., `VARCHAR(256)`) |

### Audit Fields Convention

Most entities should include four audit fields:

```csharp
[Column] public DateTime CreatedOn { get; set; }      // Set on insert (UTC)
[Column] public string CreatedBy { get; set; } = "";   // Set on insert (from caller)
[Column, Nullable] public DateTime? ModifiedOn { get; set; }  // Set on update (UTC)
[Column, Nullable] public string? ModifiedBy { get; set; }    // Set on update (from caller)
```

These are not enforced by the framework — you set them in your procedures. The convention keeps audit behavior consistent across entities.

### Indexes

Use the `[Index]` attribute from `SmartData.Server.Attributes`:

```csharp
using SmartData.Server.Attributes;

// Single column index
[Index("IX_Product_Name", nameof(Name))]

// Composite index
[Index("IX_Order_CustomerDate", nameof(CustomerId), nameof(OrderDate))]

// Unique index
[Index("IX_User_Email", nameof(Email), Unique = true)]

[Table]
public class Product { ... }
```

The `[Index]` attribute is applied to the class (not the property). Multiple `[Index]` attributes can be stacked on a single entity. Index names are prefixed with the configured `Index.Prefix` (default `"SD_"`) in the database.

### Full-Text Search

Use `[FullTextIndex]` to enable full-text search on an entity:

```csharp
using SmartData.Server.Attributes;

[Table]
[FullTextIndex(nameof(Name), nameof(Description), nameof(Notes))]
public class Product
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }
    [Column] public string Name { get; set; } = "";
    [Column, Nullable] public string? Description { get; set; }
    [Column, Nullable] public string? Notes { get; set; }
}
```

Query full-text indexes via `ctx.FullTextSearch<T>(searchTerm)` in procedures (see [IDatabaseContext API](#idatabasecontext-api)).

### Property Conventions

- **Non-nullable strings** default to `""` to avoid null-reference issues: `public string Name { get; set; } = "";`
- **Nullable properties** use `?` and `[Nullable]`: `[Column, Nullable] public string? Notes { get; set; }`
- **Relationships** are expressed via foreign key properties (e.g., `CustomerId`), not navigation properties.
- **No inheritance** — entities are plain, flat classes.

### Complete Example

```csharp
using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

namespace MyApp.Entities;

[Table]
[Index("IX_Customer_Status", nameof(Status))]
[Index("IX_Customer_Email", nameof(ContactEmail), Unique = true)]
[FullTextIndex(nameof(CompanyName), nameof(ContactName), nameof(Notes))]
public class Customer
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }

    [Column] public string CompanyName { get; set; } = "";
    [Column, Nullable] public string? Domain { get; set; }
    [Column] public string Industry { get; set; } = "";
    [Column] public string ContactName { get; set; } = "";
    [Column, MaxLength(256)] public string ContactEmail { get; set; } = "";
    [Column, Nullable] public string? ContactPhone { get; set; }
    [Column, MaxLength(32)] public string Status { get; set; } = "Active";
    [Column, Nullable] public int? OwnerId { get; set; }
    [Column, Nullable] public string? Notes { get; set; }

    [Column] public DateTime CreatedOn { get; set; }
    [Column] public string CreatedBy { get; set; } = "";
    [Column, Nullable] public DateTime? ModifiedOn { get; set; }
    [Column, Nullable] public string? ModifiedBy { get; set; }
}
```

With `SchemaMode.Auto`, this entity will automatically create a `Customer` table with all columns, a unique index on `ContactEmail`, a regular index on `Status`, and a full-text index on `CompanyName`, `ContactName`, and `Notes`.

---

## Contracts

Contracts are plain DTO classes that define the shape of procedure results. They are shared between the server (which returns them) and any client (which deserializes them).

### Folder Structure

Organize contracts **one folder per procedure**, with the folder name matching the procedure class name:

```
Contracts/
├── Common/
│   ├── SaveResult.cs
│   └── DeleteResult.cs
├── CustomerList/
│   ├── CustomerListResult.cs
│   └── CustomerItem.cs
├── CustomerGet/
│   └── CustomerGetResult.cs       (includes nested item classes)
└── ProductSave/
    └── (uses Common/SaveResult)
```

### Result Classes

Each procedure returns a typed result. List procedures typically include items, pagination, and metadata:

```csharp
namespace MyApp.Contracts.CustomerList;

public class CustomerListResult
{
    public List<CustomerItem> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
```

### Item Classes

Item classes represent individual records in a list. They contain only the fields the caller needs — not the full entity:

```csharp
namespace MyApp.Contracts.CustomerList;

public class CustomerItem
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string Industry { get; set; } = "";
    public string Status { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
}
```

### Detail Result Classes

Get procedures return a detail result, which may include related data as nested lists:

```csharp
namespace MyApp.Contracts.CustomerGet;

public class CustomerGetResult
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string Industry { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }

    // Related data
    public List<CustomerContactItem> Contacts { get; set; } = new();
    public List<CustomerNoteItem> Notes { get; set; } = new();
}

public class CustomerContactItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string Role { get; set; } = "";
    public bool IsPrimary { get; set; }
}

public class CustomerNoteItem
{
    public int Id { get; set; }
    public string Content { get; set; } = "";
    public string AuthorName { get; set; } = "";
    public DateTime CreatedOn { get; set; }
}
```

### Shared Result Types

CRUD procedures share common result types in a `Common/` folder:

```csharp
namespace MyApp.Contracts.Common;

public class SaveResult
{
    public string Message { get; set; } = "";
    public int Id { get; set; }
}

public class DeleteResult
{
    public string Message { get; set; } = "";
}
```

### Property Conventions

- **Initialize non-nullable strings** to `""`: `public string Name { get; set; } = ""`
- **Initialize collections** to empty: `public List<T> Items { get; set; } = new()`
- **Use nullable types** for optional fields: `public string? Phone { get; set; }`
- **Keep DTOs flat** — no methods, no logic, no computed properties

### How Serialization Works

SmartData's binary serializer maps properties **by name** (case-insensitive). The procedure result class and the contract class don't need to be the same type — they just need matching property names. This is what makes contracts work across project boundaries.

---

## Stored Procedures

Stored procedures are the core building block. All business logic — queries, validation, CRUD — lives in procedure classes. There are no controllers or service layers between the caller and the procedure.

### Anatomy of a Procedure

```csharp
using LinqToDB;
using MyApp.Entities;
using MyApp.Contracts.CustomerList;
using SmartData.Server.Procedures;

namespace MyApp.Procedures;

public class CustomerList : StoredProcedure<CustomerListResult>
{
    // Parameters — bound from caller arguments by name (case-insensitive)
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    // Constructor injection for dependencies
    public CustomerList(IDatabaseContext ctx) { }

    // Business logic
    public override CustomerListResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        // Build the query — filters compose into SQL, nothing loaded yet
        var query = ctx.GetTable<Customer>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var q = Search.Trim();
            query = query.Where(c => c.CompanyName.Contains(q));
        }

        var total = query.Count();
        var items = query
            .OrderBy(c => c.CompanyName)
            .Skip((Page - 1) * PageSize)
            .Take(PageSize)
            .ToList()  // Only the page is loaded into memory
            .Select(c => new CustomerItem
            {
                Id = c.Id,
                CompanyName = c.CompanyName,
                Industry = c.Industry,
                Status = c.Status,
                ContactName = c.ContactName,
                ContactEmail = c.ContactEmail
            })
            .ToList();

        return new CustomerListResult
        {
            Items = items,
            Total = total,
            Page = Page,
            PageSize = PageSize
        };
    }
}
```

### Key Concepts

**Base Class** — SmartData provides two base classes:

| Base Class | Override | Use When |
|-----------|----------|----------|
| `StoredProcedure<TResult>` | `TResult Execute(...)` | Most procedures — synchronous data access (the common case) |
| `AsyncStoredProcedure<TResult>` | `Task<TResult> ExecuteAsync(...)` | Procedures with genuine async I/O (calling external services, other procedures) |

Pick the base class that matches your needs. The framework handles the rest.

**Parameters** — Public properties on the procedure class become parameters. When a caller passes `{ Search = "acme", Page = 2 }`, SmartData binds those values to the matching properties (case-insensitive). Default values work as expected: `public int Page { get; set; } = 1;` means Page is 1 if the caller doesn't provide it.

**Constructor Injection** — Procedures are instantiated via DI (`ActivatorUtilities.CreateInstance`), so you can inject any registered service. The constructor parameter `IDatabaseContext ctx` in the examples looks redundant since `Execute` also receives `ctx` — the constructor exists solely for DI resolution. If your procedure has no other injected dependencies, the constructor body stays empty. If you need additional services (e.g., `ILogger`, a custom service), add them as constructor parameters and store them in fields:

```csharp
public class CustomerSave : StoredProcedure<SaveResult>
{
    private readonly ILogger<CustomerSave> _logger;

    public CustomerSave(IDatabaseContext ctx, ILogger<CustomerSave> logger)
    {
        _logger = logger;
    }

    public override SaveResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        _logger.LogInformation("Saving customer...");
        // ...
    }
}
```

**Return Type** — `Execute` returns `TResult` directly. The result is serialized and sent back to the caller.

### Naming Convention

Procedure class names are automatically converted to `usp_snake_case`:

| Class Name | Procedure Name |
|------------|---------------|
| `CustomerList` | `usp_customer_list` |
| `CustomerGet` | `usp_customer_get` |
| `CustomerSave` | `usp_customer_save` |
| `ContactDelete` | `usp_contact_delete` |
| `DashboardStats` | `usp_dashboard_stats` |

The `usp_` prefix stands for "user stored procedure." System procedures (built into SmartData) use the `sp_` prefix instead.

### Auto-Discovery

Procedures are discovered automatically when you call `AddStoredProcedures(assembly)`. SmartData scans the assembly for all classes that implement `IStoredProcedure` or `IAsyncStoredProcedure` (which `StoredProcedure<T>` and `AsyncStoredProcedure<T>` do respectively), converts their names, and registers them in the procedure catalog.

### Error Handling

Use `RaiseError()` to throw a `ProcedureException` with a message that is returned to the caller:

```csharp
public override CustomerGetResult Execute(IDatabaseContext ctx, CancellationToken ct)
{
    var customer = ctx.GetTable<Customer>().FirstOrDefault(x => x.Id == Id);
    if (customer == null) RaiseError($"Customer {Id} not found.");

    // Safe to use customer here — RaiseError() always throws
    return new CustomerGetResult { Id = customer.Id, ... };
}
```

`RaiseError()` is marked `[DoesNotReturn]` — it always throws and never returns. The compiler understands this, so nullable analysis works correctly after a `RaiseError()` guard.

**Signatures:**

```csharp
// Simple — message only (severity defaults to Error)
RaiseError("Customer not found.");

// With message ID and optional severity
RaiseError(1001, "Customer not found.");
RaiseError(1002, "Email already in use.", ErrorSeverity.Severe);
```

**Message IDs** use integer ranges: `0–999` is reserved for system procedures, `1000+` is for user procedures. A message ID of `0` means "no specific ID" (the default when you call `RaiseError(string)`).

**Severity levels:**

| Level | Meaning |
|-------|---------|
| `ErrorSeverity.Error` | Regular business logic error — "not found", "invalid input" (default) |
| `ErrorSeverity.Severe` | Serious — data integrity issues, unexpected state |
| `ErrorSeverity.Fatal` | Unrecoverable — procedure cannot continue safely |

All three levels throw a `ProcedureException` and halt execution. The severity tells the client how to handle the error (e.g., show a toast vs. a full error page), not whether the procedure stops.

**RPC transport:** `MessageId` and `Severity` are carried through the RPC protocol in `CommandResponse.ErrorId` and `CommandResponse.ErrorSeverity`, so remote clients can handle errors programmatically without parsing message strings.

`ProcedureException` is treated specially by the framework — its message is always returned to the caller, regardless of the `IncludeExceptionDetails` setting. Other exceptions return a generic error message in production.

### Authentication

All user procedures require a valid session token — there is no declarative way to opt out. For server-side code that needs to call a procedure without a user session (startup seeding, schedulers, internal wiring), inject `IProcedureService` instead of `IAuthenticatedProcedureService`: it bypasses the auth gate entirely and runs under framework authority (`UserId = "system"`). Don't try to reach around authentication from user-facing entry points; route unauthenticated work through trusted server-side callers.

---

## IDatabaseContext API

`IDatabaseContext` is the primary interface for data access inside procedures. It's scoped per procedure execution — each call gets a fresh instance with a pooled database connection.

### Sync vs Async

`IDatabaseContext` provides both sync and async versions of every data access method. Use sync methods in `StoredProcedure<T>` and async methods in `AsyncStoredProcedure<T>`:

| Sync (StoredProcedure) | Async (AsyncStoredProcedure) |
|------------------------|------------------------------|
| `ctx.Insert(entity)` | `await ctx.InsertAsync(entity, ct)` |
| `ctx.Update(entity)` | `await ctx.UpdateAsync(entity, ct)` |
| `ctx.Delete(entity)` | `await ctx.DeleteAsync(entity, ct)` |
| `ctx.Delete<T>(predicate)` | `await ctx.DeleteAsync<T>(predicate, ct)` |
| `ctx.FullTextSearch<T>(term)` | `await ctx.FullTextSearchAsync<T>(term, ct: ct)` |
| `ctx.GetTable<T>().ToList()` | `await ctx.GetTable<T>().ToListAsync(ct)` |

For low-concurrency apps, sync is fine and simpler. For high-concurrency production workloads, async avoids thread-pool pressure. See [Production Considerations](#production-considerations).

### Data Access (Sync)

```csharp
// Query — returns ITable<T> (IQueryable via LinqToDB)
ITable<T> GetTable<T>() where T : class, new();

// Insert — inserts entity, returns it with auto-populated Id
T Insert<T>(T entity) where T : class, new();

// Update — updates entity by primary key, returns rows affected
int Update<T>(T entity) where T : class, new();

// Delete — by entity instance
int Delete<T>(T entity) where T : class, new();

// Delete — by predicate
int Delete<T>(Expression<Func<T, bool>> predicate) where T : class, new();
```

### Data Access (Async)

```csharp
// Async versions — use in AsyncStoredProcedure<T>
Task<T> InsertAsync<T>(T entity, CancellationToken ct = default) where T : class, new();
Task<int> UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class, new();
Task<int> DeleteAsync<T>(T entity, CancellationToken ct = default) where T : class, new();
Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class, new();
```

`GetTable<T>()` returns `ITable<T>` (LinqToDB's `IQueryable`) which supports async terminal operations: `.ToListAsync(ct)`, `.FirstOrDefaultAsync(ct)`, `.CountAsync(ct)`, etc.

### Full-Text Search

```csharp
// Sync
List<T> FullTextSearch<T>(string searchTerm, int limit = 100) where T : class, new();

// Async
Task<List<T>> FullTextSearchAsync<T>(string searchTerm, int limit = 100, CancellationToken ct = default) where T : class, new();
```

### Transactions

Procedures do not run inside a transaction by default. Use `BeginTransaction()` when you need atomicity across multiple operations:

```csharp
ITransaction BeginTransaction();
```

The transaction rolls back automatically on `Dispose()` if `Commit()` was not called — the standard `using` pattern:

```csharp
public override DeleteResult Execute(IDatabaseContext ctx, CancellationToken ct)
{
    var c = ctx.GetTable<Customer>().FirstOrDefault(x => x.Id == Id);
    if (c == null) RaiseError("Customer not found.");

    using var tx = ctx.BeginTransaction();
    ctx.Delete<CustomerContact>(x => x.CustomerId == Id);
    ctx.Delete<CustomerNote>(x => x.CustomerId == Id);
    ctx.Delete(c);
    tx.Commit();
    // if anything throws before Commit(), Dispose() rolls back automatically

    return new DeleteResult { Message = "Customer deleted." };
}
```

### Procedure Execution

Call other procedures from within a procedure:

```csharp
// Execute another procedure and get typed result
Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default);

// Queue a procedure for background execution (fire-and-forget)
void QueueExecuteAsync(string spName, object? args = null);
```

### Context Properties

```csharp
string DatabaseName { get; }       // Current database name
IServiceProvider Services { get; } // Scope service provider (advanced use)
```

`IDatabaseContext` does not expose the caller's identity. If a procedure needs the acting user (e.g. for `CreatedBy`/`ModifiedBy`), declare it as a public parameter (`public string CurrentUser { get; set; } = "";`) and have the caller pass it — see "Audit Field Handling Summary" below.

### Usage Examples

**Querying:**
```csharp
// Get all active customers
var customers = ctx.GetTable<Customer>()
    .Where(c => c.Status == "Active")
    .ToList();

// Join across tables
var contacts = ctx.GetTable<CustomerContact>()
    .Where(c => c.CustomerId == customerId)
    .OrderByDescending(c => c.IsPrimary)
    .ToList();
```

**Inserting:**
```csharp
var customer = ctx.Insert(new Customer
{
    CompanyName = "Acme Corp",
    ContactEmail = "john@acme.com",
    Status = "Active",
    CreatedOn = DateTime.UtcNow,
    CreatedBy = CurrentUser   // parameter passed by caller
});
// customer.Id is now populated
```

**Updating:**
```csharp
var customer = ctx.GetTable<Customer>().First(c => c.Id == id);
customer.CompanyName = "New Name";
customer.ModifiedOn = DateTime.UtcNow;
customer.ModifiedBy = CurrentUser;   // parameter passed by caller
ctx.Update(customer);
```

**Deleting:**
```csharp
// Delete by predicate — useful for related records
ctx.Delete<CustomerContact>(c => c.CustomerId == customerId);

// Delete by entity
var customer = ctx.GetTable<Customer>().First(c => c.Id == id);
ctx.Delete(customer);
```

**Full-text search:**
```csharp
var results = ctx.FullTextSearch<Customer>("acme corp", limit: 50);
```

**Calling another procedure:**
```csharp
// Synchronous — wait for result
var stats = await ctx.ExecuteAsync<DashboardStatsResult>("usp_dashboard_stats");

// Background — fire and forget
ctx.QueueExecuteAsync("usp_send_notification", new { UserId = 42, Message = "Hello" });
```

---

## Common Patterns

### List Procedure

List procedures filter, sort, and paginate records. They return a result with items, total count, and metadata.

> **Important:** `ctx.GetTable<T>()` returns `ITable<T>` (LinqToDB's `IQueryable`). Compose your `.Where()`, `.OrderBy()`, `.Skip()`, and `.Take()` clauses **before** calling `.ToList()`. This pushes filtering to the database instead of loading every row into memory. Calling `.ToList()` first and filtering in C# works on small tables but will cause out-of-memory errors on large datasets.

```csharp
using LinqToDB;
using MyApp.Entities;
using MyApp.Contracts.CustomerList;
using SmartData.Server.Procedures;

namespace MyApp.Procedures;

public class CustomerList : StoredProcedure<CustomerListResult>
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public string? SortBy { get; set; }
    public bool SortAsc { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    public CustomerList(IDatabaseContext ctx) { }

    public override CustomerListResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        // Start with IQueryable — no data loaded yet
        var query = ctx.GetTable<Customer>().AsQueryable();

        // Filter — composes into the SQL WHERE clause
        if (!string.IsNullOrWhiteSpace(Status) && Status != "All")
            query = query.Where(c => c.Status == Status);

        // Search — database-side LIKE/contains
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var q = Search.Trim();
            query = query.Where(c =>
                c.CompanyName.Contains(q) ||
                c.ContactName.Contains(q) ||
                c.ContactEmail.Contains(q));
        }

        // Sort — composes into SQL ORDER BY
        query = SortBy switch
        {
            "name" => SortAsc ? query.OrderBy(c => c.CompanyName) : query.OrderByDescending(c => c.CompanyName),
            "status" => SortAsc ? query.OrderBy(c => c.Status) : query.OrderByDescending(c => c.Status),
            _ => query.OrderBy(c => c.CompanyName)
        };

        // Count — executes a SELECT COUNT(*) with the filters applied
        var total = query.Count();

        // Paginate — only the requested page is fetched from the database
        var paged = query
            .Skip((Page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        return new CustomerListResult
        {
            Items = paged.Select(c => new CustomerItem
            {
                Id = c.Id,
                CompanyName = c.CompanyName,
                Industry = c.Industry,
                Status = c.Status,
                ContactName = c.ContactName,
                ContactEmail = c.ContactEmail
            }).ToList(),
            Total = total,
            Page = Page,
            PageSize = PageSize
        };
    }
}
```

If you need aggregate counts (e.g., "how many Active, how many Trial") that span the full unfiltered dataset, run a separate targeted query rather than loading all rows:

```csharp
// Targeted count queries — each runs as SELECT COUNT(*) WHERE ...
var allCount = ctx.GetTable<Customer>().Count();
var activeCount = ctx.GetTable<Customer>().Count(c => c.Status == "Active");
var trialCount = ctx.GetTable<Customer>().Count(c => c.Status == "Trial");
```

### Get Procedure

Get procedures load a single entity by ID, validate it exists, load related data, and return a detail result.

```csharp
using LinqToDB;
using MyApp.Entities;
using MyApp.Contracts.CustomerGet;
using SmartData.Server.Procedures;

namespace MyApp.Procedures;

public class CustomerGet : StoredProcedure<CustomerGetResult>
{
    public int Id { get; set; }

    public CustomerGet(IDatabaseContext ctx) { }

    public override CustomerGetResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var c = ctx.GetTable<Customer>().FirstOrDefault(x => x.Id == Id);
        if (c == null) RaiseError($"Customer {Id} not found.");

        // Load related data — filter and sort in the query, then materialize
        var contacts = ctx.GetTable<CustomerContact>()
            .Where(x => x.CustomerId == c.Id)
            .OrderByDescending(x => x.IsPrimary)
            .ToList()
            .Select(x => new CustomerContactItem
            {
                Id = x.Id,
                Name = x.Name,
                Email = x.Email,
                Phone = x.Phone,
                Role = x.Role,
                IsPrimary = x.IsPrimary
            })
            .ToList();

        var notes = ctx.GetTable<CustomerNote>()
            .Where(x => x.CustomerId == c.Id)
            .OrderByDescending(x => x.CreatedOn)
            .ToList()
            .Select(x => new CustomerNoteItem
            {
                Id = x.Id,
                Content = x.Content,
                AuthorName = x.AuthorName,
                CreatedOn = x.CreatedOn
            })
            .ToList();

        return new CustomerGetResult
        {
            Id = c.Id,
            CompanyName = c.CompanyName,
            Industry = c.Industry,
            ContactName = c.ContactName,
            ContactEmail = c.ContactEmail,
            Status = c.Status,
            CreatedOn = c.CreatedOn,
            CreatedBy = c.CreatedBy,
            ModifiedOn = c.ModifiedOn,
            ModifiedBy = c.ModifiedBy,
            Contacts = contacts,
            Notes = notes
        };
    }
}
```

### Save Procedure

Save procedures handle both insert and update. The convention is to use a nullable `Id` — null or 0 means insert, a positive value means update.

```csharp
using LinqToDB;
using MyApp.Entities;
using MyApp.Contracts.Common;
using SmartData.Server.Procedures;

namespace MyApp.Procedures;

public class CustomerSave : StoredProcedure<SaveResult>
{
    public int? Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string Industry { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string ContactEmail { get; set; } = "";
    public string? ContactPhone { get; set; }
    public string Status { get; set; } = "Active";
    public string CurrentUser { get; set; } = "";   // caller passes the app user identity

    public CustomerSave(IDatabaseContext ctx) { }

    public override SaveResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (Id.HasValue && Id.Value > 0)
        {
            // Update
            var c = ctx.GetTable<Customer>().FirstOrDefault(x => x.Id == Id.Value);
            if (c == null) RaiseError($"Customer {Id} not found.");

            c.CompanyName = CompanyName;
            c.Industry = Industry;
            c.ContactName = ContactName;
            c.ContactEmail = ContactEmail;
            c.ContactPhone = ContactPhone;
            c.Status = Status;
            c.ModifiedOn = now;
            c.ModifiedBy = CurrentUser;
            ctx.Update(c);

            return new SaveResult { Message = "Customer updated.", Id = c.Id };
        }
        else
        {
            // Insert
            var c = ctx.Insert(new Customer
            {
                CompanyName = CompanyName,
                Industry = Industry,
                ContactName = ContactName,
                ContactEmail = ContactEmail,
                ContactPhone = ContactPhone,
                Status = Status,
                CreatedOn = now,
                CreatedBy = CurrentUser
            });

            return new SaveResult { Message = "Customer created.", Id = c.Id };
        }
    }
}
```

### Delete Procedure

Delete procedures validate the entity exists, clean up related records, then delete.

```csharp
using LinqToDB;
using MyApp.Entities;
using MyApp.Contracts.Common;
using SmartData.Server.Procedures;

namespace MyApp.Procedures;

public class CustomerDelete : StoredProcedure<DeleteResult>
{
    public int Id { get; set; }

    public CustomerDelete(IDatabaseContext ctx) { }

    public override DeleteResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var c = ctx.GetTable<Customer>().FirstOrDefault(x => x.Id == Id);
        if (c == null) RaiseError($"Customer {Id} not found.");

        // Delete related records first
        ctx.Delete<CustomerContact>(x => x.CustomerId == Id);
        ctx.Delete<CustomerNote>(x => x.CustomerId == Id);

        // Delete the entity
        ctx.Delete(c);

        return new DeleteResult { Message = "Customer deleted." };
    }
}
```

### Audit Field Handling Summary

| Operation | `CreatedOn` | `CreatedBy` | `ModifiedOn` | `ModifiedBy` |
|-----------|-------------|-------------|--------------|--------------|
| Insert | `DateTime.UtcNow` | `CurrentUser` (parameter) | — | — |
| Update | *don't touch* | *don't touch* | `DateTime.UtcNow` | `CurrentUser` (parameter) |

The `CurrentUser` parameter is a public property on the procedure, passed by the caller (controller, service, etc.). This keeps procedures decoupled from ASP.NET Core — they work the same whether called from an MVC controller, a background job, or a CLI tool.

---

## Calling Procedures

There are two ways to call procedures: **directly** (in-process) and **remotely** (over HTTP).

### Direct: IProcedureService vs IAuthenticatedProcedureService

SmartData registers **two** scoped procedure callers, separated by trust boundary:

| Service | Authority | Auth gate | Use from |
|---------|-----------|-----------|----------|
| `IProcedureService` | Framework (full admin) | **Bypassed** — no session, no permission check. `UserId` = `"system"` | Trusted server-side code: schedulers, startup tasks, internal wiring |
| `IAuthenticatedProcedureService` | Per-user session | **Enforced** — unauthenticated calls are rejected. Permission checks live inside system procedures. | User-facing entry points: the `/rpc` endpoint (wired automatically), the embedded admin console, your own authenticated controllers |

Pick by answering: *"Does this caller represent a specific end user, or is it my server acting on its own authority?"* If it's the server, use `IProcedureService` and nothing can be misconfigured into a privilege leak. If it's a user, use `IAuthenticatedProcedureService` and the permission system runs normally.

**Interfaces:**
```csharp
public interface IProcedureService
{
    Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default);
    void QueueExecuteAsync(string spName, object? args = null);
}

public interface IAuthenticatedProcedureService
{
    void Authenticate(string? token);

    Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default);
    void QueueExecuteAsync(string spName, object? args = null);
}
```

Both are scoped; a scope represents one caller. The caller's target database is picked by each procedure internally (via `db.UseDatabase(...)`), not by the service — pass a `Database` parameter in `args` if the procedure declares one. For `IAuthenticatedProcedureService`, call `Authenticate` once per scope, then call `ExecuteAsync` as needed.

**MVC Controller Example:**
```csharp
public class CustomersController : Controller
{
    private readonly IProcedureService _procedures;

    public CustomersController(IProcedureService procedures)
    {
        _procedures = procedures;
    }

    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var result = await _procedures.ExecuteAsync<CustomerListResult>(
            "usp_customer_list",
            new { Search = search, Page = page, PageSize = 20 });

        return View(result);
    }

    public async Task<IActionResult> Detail(int id)
    {
        var result = await _procedures.ExecuteAsync<CustomerGetResult>(
            "usp_customer_get",
            new { Id = id });

        return View(result);
    }

    [HttpPost]
    public async Task<IActionResult> Save(CustomerSaveViewModel model)
    {
        var result = await _procedures.ExecuteAsync<SaveResult>(
            "usp_customer_save",
            new
            {
                model.Id,
                model.CompanyName,
                model.Industry,
                model.ContactName,
                model.ContactEmail,
                model.Status,
                CurrentUser = User.Identity?.Name ?? "unknown"
            });

        return RedirectToAction("Detail", new { id = result.Id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await _procedures.ExecuteAsync<DeleteResult>(
            "usp_customer_delete",
            new { Id = id });

        return RedirectToAction("Index");
    }
}
```

**Background execution:**
```csharp
// Fire and forget — runs on a background thread
_procedures.QueueExecuteAsync("usp_send_weekly_report", new { Week = 42 });
```

### Remote: SmartDataClient

For applications where the frontend and backend are separate processes. `SmartDataClient` sends binary RPC requests to the `POST /rpc` endpoint.

**Setup:**
```csharp
var client = new SmartDataClient("http://localhost:5124");
client.Database = "master";
client.Token = "session-token-here";
```

**Calling procedures:**
```csharp
// Send a command and get the raw response
var response = await client.SendAsync("usp_customer_list", new Dictionary<string, object>
{
    ["Search"] = "acme",
    ["Page"] = 1,
    ["PageSize"] = 20
});

if (response.Success)
{
    var result = response.GetData<CustomerListResult>();
    // Use result.Items, result.Total, etc.
}
else
{
    // response.Error contains the error message
}
```

**SmartDataClient API:**

| Member | Purpose |
|--------|---------|
| `Token` | Session token for authentication (set after login) |
| `Database` | Target database name (default: `"master"`) |
| `SendAsync(command, args)` | Sends a binary RPC request, returns `CommandResponse` |

**CommandResponse API:**

| Member | Purpose |
|--------|---------|
| `Success` | `true` if the procedure executed without error |
| `Error` | Error message (when `Success` is `false`) |
| `Authenticated` | Whether the token was valid (`true`/`false`/`null` if no token sent) |
| `GetData<T>()` | Deserializes the result data into type `T` |
| `GetDataAsJson()` | Converts the result data to a JSON string |

---

## Scheduling

SmartData includes a first-class scheduler for running procedures on a timer. The design is opinionated:

> **A scheduled job is a stored procedure with a `[Daily]` on it. Developers change what it does; users change when it runs.**

No extra project, no separate "job definition" language, no state machines in the database. You decorate a procedure, call `AddSmartDataScheduler()`, and the framework takes it from there.

### Minimal example

```csharp
using SmartData.Core;
using SmartData.Server.Procedures;
using SmartData.Server.Scheduling.Attributes;

[Job("Nightly DB Maintenance", Category = "Ops",
     Description = "Vacuums stale rows and reindexes hot tables.")]
[Daily("03:15")]
[Retry(attempts: 3, intervalSeconds: 60)]
public class NightlyCleanup : AsyncStoredProcedure<VoidResult>
{
    public override async Task<VoidResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
    {
        // ... your work ...
        return VoidResult.Instance;
    }
}
```

Register the scheduler in `Program.cs` — **after** `AddStoredProcedures` so your assembly is visible to the reconciler:

```csharp
builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
builder.Services.AddSmartDataScheduler();
```

On startup the reconciler creates a `_sys_schedules` row for `NightlyCleanup`. From then on, the `JobScheduler` hosted service polls `sp_scheduler_tick` every 15s (default), claims due schedules, and executes them.

### Schedule attributes

All times are **server local time**. Calendar filters (`Days`, `Months`, `Weeks`, `Between`) are `[Flags]` enum properties that compose onto any cadence.

| Attribute | Example | Meaning |
|-----------|---------|---------|
| `[Daily]` | `[Daily("02:00")]`, `[Daily(2, 0)]` | Once per day at `HH:mm`. Add `Days = Days.Weekdays` to narrow. |
| `[Every]` | `[Every(5, Unit.Minutes)]` | Wall-clock anchors — fires at `:00`, `:05`, `:10`, … `Between = "09:00-17:00"` bounds a daily window; combine with `Days` for business hours. |
| `[Weekly]` | `[Weekly(Days.Mon \| Days.Fri, "06:00")]` | Weekly at a given time on selected weekdays. `Every = N` for biweekly. |
| `[Monthly]` | `[Monthly(Day.D1 \| Day.Last, "00:30")]` | Specific calendar days. `Day.Last` means end-of-month; `D29/30/31` silently skip months they don't exist in. |
| `[MonthlyDow]` | `[MonthlyDow(Weeks.First, Days.Mon, "06:00")]` | Nth weekday of month — "first Monday", "last Friday", etc. |
| `[Once]` | `[Once("2026-06-01 09:00")]` | One-shot. Schedule auto-disables after it fires. |
| `[Job]` | `[Job("Name", Category = "Ops", Description = "…")]` | **Code-only** display metadata — never persisted. |
| `[Retry]` | `[Retry(attempts: 3, intervalSeconds: 60)]` | See the retry section below. |

Stack multiple attributes to fire at multiple times — `[Daily("09:00")] [Daily("17:00")]` produces two rows named `Daily_09_00` and `Daily_17_00`. Reordering the attributes doesn't change those names, so user customizations stay attached.

### Retry semantics

> ⚠️ **Read this carefully — convention differs from most retry libraries.** `[Retry(attempts: 3)]` means **3 total runs** (1 initial + 2 retries), not "3 retries after the first." `attempts: 1` is equivalent to no retry.

Retry is a row edit, not a queue. On failure, `sp_schedule_execute` writes `NextAttemptAt` on the failed run; the next tick picks it up and fires a fresh run with `AttemptNumber + 1`. `ErrorSeverity.Fatal` always short-circuits retry.

### Developer/user split

| Area | Owner | Mechanism |
|------|-------|-----------|
| Which procedures are schedulable | Developer | `[Daily]`/`[Every]`/… attribute on the class |
| What a procedure does | Developer | The procedure's `ExecuteAsync` body |
| **When** a schedule fires | Developer | Attribute arguments — overwritten into DB on every startup |
| Whether a schedule is enabled | User | `sp_schedule_update` (or console toggle) |
| Retry attempts / interval / jitter | User | `sp_schedule_update` — preserved across reconciles |

Code is the source of truth for timing. Users cannot change *when* a job fires from the console — that requires editing the attribute and restarting. The reconciler always overwrites timing fields from the attribute; it preserves only the four user-controlled fields (`Enabled`, `RetryAttempts`, `RetryIntervalSeconds`, `JitterSeconds`). Removing an attribute disables the row (history is retained).

### Manual trigger, cancel, history

- **Run now**: `sp_schedule_start` claims a run immediately, outside the schedule timeline.
- **Cancel**: `sp_schedule_cancel` signals a cooperative cancel. The in-flight procedure detects it within a few seconds and throws `OperationCanceledException`.
- **History**: `sp_schedule_history` returns run records filterable by outcome/procedure/date — each record includes start/finish times, duration, outcome, message, attempt number, and originating instance id.

### Multi-instance safety

Running multiple SmartData servers against the same database is safe: only one instance can claim a given fire, long-running jobs on one node are never mistaken for crashes on another, and cancels work across nodes. The implementation details (unique-index claim, heartbeat-based liveness, orphan sweep) live in [SmartData.Server.md — Scheduling](SmartData.Server.md).

### Catch-up policy

If the scheduler was down when a schedule was due, the default is to **drop** the missed fire and roll `NextRunOn` forward. Set `SchedulerOptions.MaxCatchUp` to a small integer to queue up to that many missed fires after recovery — only safe for idempotent jobs. Proposal rationale: replaying hours of accumulated fires is almost always wrong (duplicated reports, re-sent emails).

### Configuration

```csharp
builder.Services.AddSmartDataScheduler(o =>
{
    o.Enabled              = true;
    o.PollInterval         = TimeSpan.FromSeconds(15);
    o.MaxConcurrentRuns    = 4;
    o.HistoryRetentionDays = 30;    // built-in sp_schedule_run_retention
    o.HeartbeatInterval    = TimeSpan.FromSeconds(3);
    o.OrphanTimeout        = TimeSpan.FromMinutes(5);
    o.MaxCatchUp           = 0;     // 0 = drop missed; >0 = queue up to N
});
```

Setting `Enabled = false` keeps reconciliation running (schedules remain visible in `sp_schedule_list`) but stops the tick — handy for deploying code to non-scheduler nodes without losing visibility.

### Admin UI

SmartData.Console ships with a `/console/schedulers` area for list/detail/history/stats, plus toggle/start/cancel/delete buttons. See `docs/SmartData.Console.md` → *Schedulers*.

### What the scheduler deliberately does not do

- **No multi-step jobs.** Workflow is composed in C# via `ctx.ExecuteAsync<T>()` or direct `await` calls. Re-implementing `OnSuccess` / `OnFailure` / `GoToStep` in database rows would be a worse programming language running inside the database.
- **No runtime procedure registration.** Schedules for non-existent procedures are rejected. The set of schedulable procedures is closed to what's in code.
- **No arguments to target procedures.** Scheduled calls pass nothing. If a job needs configuration, read it from `Setting`/`SettingValue` inside `ExecuteAsync`.
- **No per-schedule timezone.** All times are server local. Run the process in UTC if you need UTC-stable semantics.

For the full design (entities, reconciliation rules, execution path, production notes), see [docs/SmartData.Server.md](SmartData.Server.md) → *Scheduling*.

---

## RPC Protocol

SmartData uses a custom binary RPC protocol over a single HTTP endpoint. Understanding the flow helps when debugging or building custom integrations.

### Trade-offs vs REST / gRPC

The single-endpoint binary protocol is compact and simple to implement, but it trades away things you get for free with REST or gRPC:

- **No HTTP caching** — Every call is `POST /rpc`, so HTTP caches, CDNs, and `ETag`/`304` responses don't apply.
- **Opaque to browser devtools** — Request/response bodies are binary, not JSON. You can't inspect payloads in the Network tab. Use `CommandResponse.GetDataAsJson()` server-side for debugging.
- **No curl debugging** — You can't test procedures with a quick `curl` command. Use `IProcedureService` directly or build a small test harness.
- **Standard middleware doesn't compose** — ASP.NET Core middleware that inspects request/response bodies (logging, rate limiting by endpoint) sees a single opaque route.

These are acceptable trade-offs for many applications, but worth understanding if you're evaluating SmartData against a REST or gRPC architecture.

### Request Flow

```
Client                          Server
  │                               │
  │  POST /rpc                    │
  │  Body: BinarySerialize(       │
  │    CommandRequest {            │
  │      Command: "usp_...",      │
  │      Token: "...",            │
  │      Database: "master",      │
  │      Args: BinarySerialize(   │
  │        { key: value, ... })   │
  │    })                         │
  │ ─────────────────────────────>│
  │                               │  CommandRouter.RouteAsync()
  │                               │    ├── Deserialize args
  │                               │    ├── Validate token
  │                               │    └── ProcedureExecutor.ExecuteAsync()
  │                               │          ├── Resolve procedure from catalog
  │                               │          ├── Create DI scope
  │                               │          ├── Instantiate procedure class
  │                               │          ├── Check auth/permissions
  │                               │          ├── Bind parameters to properties
  │                               │          └── Call ExecuteAsync()
  │                               │
  │  Response:                    │
  │  BinarySerialize(             │
  │    CommandResponse {           │
  │      Success: true/false,     │
  │      Data: BinarySerialize(   │
  │        result),               │
  │      Error: "..." or null,    │
  │      Authenticated: true/null │
  │    })                         │
  │ <─────────────────────────────│
```

### Wire Format

- Content type: `application/x-binaryrpc`
- Both request and response bodies are serialized using `BinarySerializer` from SmartData.Core
- Arguments are double-serialized: the `Args` field in `CommandRequest` is itself a binary-serialized dictionary
- Properties are matched by name (case-insensitive) during deserialization

### CommandRequest

```csharp
public class CommandRequest
{
    public string Command { get; set; } = "";    // Procedure name (e.g., "usp_customer_list")
    public string? Token { get; set; }           // Session token
    public string? Database { get; set; }        // Target database (default: "master")
    public byte[]? Args { get; set; }            // Binary-serialized argument dictionary
}
```

### CommandResponse

```csharp
public class CommandResponse
{
    public bool Success { get; set; }            // Did the procedure succeed?
    public byte[]? Data { get; set; }            // Binary-serialized result object
    public string? Error { get; set; }           // Error message (on failure)
    public int? ErrorId { get; set; }            // Message ID (0–999 system, 1000+ user)
    public int? ErrorSeverity { get; set; }      // 0=Error, 1=Severe, 2=Fatal
    public bool? Authenticated { get; set; }     // Token validation result

    public T? GetData<T>();                      // Deserialize Data into T
    public string? GetDataAsJson();              // Convert Data to JSON
}
```

### Error Handling in the RPC Layer

| Exception Type | Behavior |
|---------------|----------|
| `ProcedureException` | Message, ErrorId, and ErrorSeverity returned to client (user-facing errors) |
| `UnauthorizedAccessException` | Logged + generic message (unless `IncludeExceptionDetails` is true) |
| Any other exception | Logged + generic "An internal error occurred." (unless `IncludeExceptionDetails` is true) |

### Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/rpc` | POST | Binary RPC — all procedure calls go here |
| `/health` | GET | Health check — returns JSON with status and diagnostics |

---

## Production Considerations

### Thread-Pool Pressure (Sync vs Async)

`StoredProcedure<T>` with sync data access methods (`Insert`, `Update`, `Delete`, `GetTable().ToList()`) blocks a thread-pool thread for every database roundtrip. Under low concurrency this is invisible. Under high concurrency (hundreds of concurrent requests), thread-pool starvation becomes a real risk.

For high-concurrency workloads, use `AsyncStoredProcedure<T>` with the async data access methods (`InsertAsync`, `UpdateAsync`, `DeleteAsync`, `GetTable().ToListAsync()`). These release the thread back to the pool during I/O, allowing the server to handle more concurrent requests with fewer threads.

For low-concurrency apps, sync is simpler and performs fine.

### Transactions

Procedures do **not** run inside a database transaction by default. Each `Insert`, `Update`, and `Delete` is an independent operation. If a procedure performs multiple mutations and one fails mid-way, the database is left in a partially modified state.

Use `ctx.BeginTransaction()` to wrap multi-step mutations in a transaction. The transaction rolls back automatically if `Commit()` is not called before `Dispose()`. See [Transactions](#transactions) in the IDatabaseContext API section for the full pattern.

### Schema Migration in Production

`SchemaMode.Auto` compares entity classes to the database on first use and automatically creates tables and adds columns. It does **not** drop columns, rename columns, or migrate data. If you rename a property, you get a new column and the old one remains with stale data.

For production deployments, consider:
- Using `SchemaMode.Manual` and managing schema changes explicitly
- Testing schema changes against a copy of production data before deploying
- Using the SmartData CLI (`sd.exe`) for explicit schema operations

---

## What This Guide Doesn't Cover

This guide covers the core patterns for building with SmartData. The following topics are real concerns in production applications but are not covered here:

- **Authorization** — SmartData enforces permission checks inside its own system procedures (scoped and wildcard matching included), but this mechanism is framework-internal and not exposed to user procedures. Handle authorization for your own procedures in your application layer. User procedures cannot opt out of authentication declaratively; route unauthenticated server-side work through `IProcedureService` instead.
- **Input validation** — The examples don't validate input beyond null-checking entity existence. In production, validate parameters before writing to the database (length limits, format checks, business rules).
- **Optimistic concurrency** — The entity examples don't include a `RowVersion` or `ModifiedOn`-based concurrency check. If two users edit the same record simultaneously, the last write wins silently.
- **Testing procedures** — There's no built-in test harness for procedures. Testing strategies (in-memory database, integration tests against SQLite) are left to the developer.
- **Logging conventions** — SmartData logs procedure execution and errors internally, but the guide doesn't cover how to add structured logging to your own procedures.
- **System procedures** — SmartData includes 35+ built-in system procedures for schema management, backups, user management, and diagnostics. See the [SmartData.Server reference docs](SmartData.Server.md) for the full list.
- **SmartData Console** — An embedded admin UI for database inspection. See the [SmartData.Console docs](SmartData.Console.md).
