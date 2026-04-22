---
title: Define a procedure
description: Create a new StoredProcedure class and register it.
---

Create a class that inherits `StoredProcedure<TResult>`. Add public properties for parameters. Override `Execute`.

```csharp
using SmartData.Server.Procedures;
using MyApp.Entities;
using MyApp.Contracts.CustomerList;

namespace MyApp.Procedures;

public class CustomerList : StoredProcedure<CustomerListResult>
{
    public string? Search   { get; set; }
    public int     Page     { get; set; } = 1;
    public int     PageSize { get; set; } = 20;

    public CustomerList(IDatabaseContext ctx) { }

    public override CustomerListResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var query = ctx.GetTable<Customer>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
            query = query.Where(c => c.CompanyName.Contains(Search));

        var total = query.Count();
        var items = query
            .OrderBy(c => c.CompanyName)
            .Skip((Page - 1) * PageSize)
            .Take(PageSize)
            .ToList();

        return new CustomerListResult
        {
            Items    = items.Select(c => new CustomerItem { /* ... */ }).ToList(),
            Total    = total,
            Page     = Page,
            PageSize = PageSize
        };
    }
}
```

That's it. The class name `CustomerList` registers as `usp_customer_list` — callers invoke it by that name.

## One more step — register the assembly

In `Program.cs`:

```csharp
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
```

Every `StoredProcedure<T>` / `AsyncStoredProcedure<T>` in the assembly auto-registers.

## When to pick async

Use `AsyncStoredProcedure<T>` when you have genuine async I/O — another procedure call, outbound HTTP, a queue. Pure database work is fine sync unless you're running hot.

```csharp
public class FetchAndStore : AsyncStoredProcedure<SaveResult>
{
    public FetchAndStore(IDatabaseContext ctx) { }

    public override async Task<SaveResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
    {
        var data = await ctx.ExecuteAsync<SomeResult>("usp_other", new { /* ... */ });
        // ...
        return new SaveResult { /* ... */ };
    }
}
```

## Related

- [Procedures](/fundamentals/procedures/) — the mental model
- [Return DTOs, not entities](/how-to/return-dtos-not-entities/) — what `TResult` should look like
- [Database context](/fundamentals/database-context/) — the `ctx` surface
