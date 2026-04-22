---
title: Your first procedure
description: Define an entity, define a procedure, call it. Ten minutes end-to-end.
---

One entity, one procedure, one local call. The RPC layer waits until [Your first RPC call](/get-started/your-first-rpc-call/) — here we stay in-process so you can focus on the procedure shape.

Prereqs: .NET 10 SDK, [install](/get-started/install/) done so package references resolve.

## 1. Create the project

```bash
dotnet new web -n HelloSmartData
cd HelloSmartData

dotnet add package SmartData.Server.Sqlite
```

`SmartData.Server.Sqlite` transitively pulls in `SmartData.Server` and `SmartData.Core`.

## 2. Wire it up

Replace `Program.cs`:

```csharp
using SmartData;                     // AddSmartData, AddStoredProcedures, UseSmartData
using SmartData.Server.Sqlite;       // AddSmartDataSqlite

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);

var app = builder.Build();
app.UseSmartData();   // maps POST /rpc + GET /health
app.Run();
```

Three service calls and one middleware line. `SchemaMode` defaults to `Auto` — the first call to each entity creates/migrates its table. Fine for a tutorial; see [Providers](/fundamentals/providers/) for production guidance.

## 3. Define an entity

`Entities/Customer.cs`:

```csharp
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

namespace HelloSmartData.Entities;

[Table]
[Index("IX_Customer_Name", nameof(CompanyName))]
public class Customer
{
    [PrimaryKey, Identity] public int    Id          { get; set; }
    [Column]               public string CompanyName { get; set; } = "";
    [Column]               public string? City       { get; set; }
}
```

`[Table]` / `[Column]` / `[PrimaryKey, Identity]` come from LinqToDB. `[Index]` is SmartData's — AutoRepo provisions the index on first use. See [Entities](/fundamentals/entities/) for the full attribute surface.

## 4. Define a procedure

`Procedures/CustomerList.cs`:

```csharp
using SmartData.Server.Procedures;
using HelloSmartData.Entities;

namespace HelloSmartData.Procedures;

public class CustomerListResult
{
    public List<Customer> Items { get; set; } = new();
    public int            Total { get; set; }
}

public class CustomerList : StoredProcedure<CustomerListResult>
{
    public string? Search { get; set; }

    public CustomerList(IDatabaseContext ctx) { }

    public override CustomerListResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var query = ctx.GetTable<Customer>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
            query = query.Where(c => c.CompanyName.Contains(Search));

        var items = query.OrderBy(c => c.CompanyName).ToList();
        return new CustomerListResult { Items = items, Total = items.Count };
    }
}
```

Four things to notice:

1. Class name `CustomerList` auto-registers as `usp_customer_list`.
2. `Search` is a public property — callers bind to it by name, case-insensitive. Missing args keep their defaults.
3. The `IDatabaseContext` constructor parameter is required for DI to resolve the class, even though `Execute` also gets a fresh `ctx`.
4. Compose `.Where` / `.OrderBy` **before** `.ToList()` so filtering pushes to SQL.

For a tutorial we're returning the entity directly; production code should return DTOs — see [Return DTOs, not entities](/how-to/return-dtos-not-entities/).

## 5. Call it

Skip HTTP entirely. Inject `IProcedureService` into a minimal endpoint and call the procedure by name. Add these two `using` lines at the top of `Program.cs`:

```csharp
using SmartData.Core;                // VoidResult
using SmartData.Server;              // IProcedureService
using HelloSmartData.Procedures;     // CustomerListResult
```

Then, before `app.Run()`:

```csharp
app.MapGet("/demo", async (IProcedureService procs) =>
{
    // Seed one row so the list isn't empty
    await procs.ExecuteAsync<VoidResult>("usp_customer_seed");

    var result = await procs.ExecuteAsync<CustomerListResult>(
        "usp_customer_list",
        new { Search = "acme" });

    return result.Items;
});
```

And a matching seed procedure `Procedures/CustomerSeed.cs`:

```csharp
using SmartData.Server.Procedures;
using SmartData.Core;
using HelloSmartData.Entities;

namespace HelloSmartData.Procedures;

public class CustomerSeed : StoredProcedure<VoidResult>
{
    public CustomerSeed(IDatabaseContext ctx) { }

    public override VoidResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        if (!ctx.GetTable<Customer>().Any())
        {
            ctx.Insert(new Customer { CompanyName = "Acme Corp",  City = "Springfield" });
            ctx.Insert(new Customer { CompanyName = "Globex",     City = "Cypress Creek" });
            ctx.Insert(new Customer { CompanyName = "Acme Labs",  City = "Portland" });
        }
        return new VoidResult();
    }
}
```

`IProcedureService` runs under framework authority (`UserId = "system"`, auth gate bypassed) — exactly what you want for startup seeding and trusted server-side work. User-facing callers go through `IAuthenticatedProcedureService` and that's what `POST /rpc` is wired to automatically. The trust split is covered in [Procedures → Two callers, one boundary](/fundamentals/procedures/#two-callers-one-boundary).

## 6. Run it

```bash
dotnet run
```

Watch the console for the port Kestrel bound to (`Now listening on: http://localhost:5219` or similar), then:

```bash
curl http://localhost:<port>/demo
```

First call creates `data/master.db`, provisions the `Customer` table + index, inserts three rows, and returns the two with "acme" in the name. Change the `["Search"]` value in `Program.cs`, restart, and re-hit `/demo` to see filtering land in SQL.

## Where to go next

- **Call it remotely.** [Your first RPC call](/get-started/your-first-rpc-call/) — swap `IProcedureService` for a `SmartDataClient` over HTTP.
- **Four-procedure CRUD.** [Build a CRUD app](/tutorials/build-a-crud-app/) extends this with save, delete, DTOs, and a client project.
- **Mental model.** [Procedures](/fundamentals/procedures/) — naming, registration, lifecycle, errors.
- **What `ctx` can do.** [Database context](/fundamentals/database-context/) — `Insert` / `Update` / `Delete`, transactions, sub-procedure calls.
