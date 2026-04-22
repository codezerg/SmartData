---
title: Database context
description: IDatabaseContext — the per-call data access surface.
---

`IDatabaseContext` is the API you use inside a procedure to read and write data. It's **scoped per procedure execution** — each call gets a fresh context with a pooled connection. You never construct one; it arrives as a parameter to `Execute` / `ExecuteAsync`.

```csharp
public override CustomerListResult Execute(IDatabaseContext ctx, CancellationToken ct)
{
    var items = ctx.GetTable<Customer>()
        .Where(c => c.Status == "Active")
        .OrderBy(c => c.CompanyName)
        .ToList();

    return new CustomerListResult { /* ... */ };
}
```

## Sync vs async

Every data-access method has both a sync and an async form. The rule is mechanical — **match the base class**:

| `StoredProcedure<T>` (sync) | `AsyncStoredProcedure<T>` (async) |
| --- | --- |
| `ctx.Insert(entity)` | `await ctx.InsertAsync(entity, ct)` |
| `ctx.Update(entity)` | `await ctx.UpdateAsync(entity, ct)` |
| `ctx.Delete(entity)` | `await ctx.DeleteAsync(entity, ct)` |
| `ctx.Delete<T>(pred)` | `await ctx.DeleteAsync<T>(pred, ct)` |
| `ctx.FullTextSearch<T>(term)` | `await ctx.FullTextSearchAsync<T>(term, ct: ct)` |
| `query.ToList()` | `await query.ToListAsync(ct)` |

Low-concurrency app? Sync is fine and simpler. Heavy concurrent load? Async frees the thread during I/O and avoids thread-pool pressure. Don't mix: a sync procedure calling `.ToListAsync().Result` is the worst of both worlds.

## Querying

`ctx.GetTable<T>()` returns `ITable<T>` — LinqToDB's `IQueryable<T>`. Compose `.Where` / `.OrderBy` / `.Skip` / `.Take` **before** terminating with `.ToList()` / `.First()` / `.Count()` so filters push down to SQL.

```csharp
// GOOD — pushes filter + paging to the database
var items = ctx.GetTable<Customer>()
    .Where(c => c.Status == "Active" && c.Industry == industry)
    .OrderBy(c => c.CompanyName)
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToList();

// BAD — loads the whole table into memory, then filters in C#
var all = ctx.GetTable<Customer>().ToList();
var items = all.Where(c => c.Status == "Active").ToList();
```

This is the single biggest performance trap in a framework that looks like "just use LINQ".

## Mutating

```csharp
// Insert — returns the entity with its Id populated
var c = ctx.Insert(new Customer { CompanyName = "Acme", /* ... */ });

// Update — by primary key
customer.ModifiedOn = DateTime.UtcNow;
ctx.Update(customer);

// Delete — by entity
ctx.Delete(customer);

// Delete — by predicate, useful for related rows
ctx.Delete<CustomerContact>(x => x.CustomerId == id);
```

## Transactions

Procedures **do not** run inside a transaction. Each `Insert` / `Update` / `Delete` is its own commit. For atomic multi-step work, open one explicitly with `using`:

```csharp
using var tx = ctx.BeginTransaction();
ctx.Delete<CustomerContact>(x => x.CustomerId == Id);
ctx.Delete<CustomerNote>(x => x.CustomerId == Id);
ctx.Delete(customer);
tx.Commit();
// If anything throws before Commit(), Dispose() rolls back automatically.
```

Keep transaction scopes short. Database-transaction discipline hasn't changed.

## Full-text search

```csharp
var matches = ctx.FullTextSearch<Customer>("acme corp", limit: 50);
```

The entity must have a `[FullTextIndex(...)]` attribute — see [Entities & AutoRepo](/fundamentals/entities/).

## Calling other procedures

You can invoke another procedure from inside this one:

```csharp
// Synchronous — wait for result
var stats = await ctx.ExecuteAsync<DashboardStatsResult>("usp_dashboard_stats");

// Background — fire-and-forget, runs on the queue
ctx.QueueExecuteAsync("usp_send_notification", new { UserId = 42, Message = "hi" });
```

Both go through the catalog, bind args by name, and run under the same authority as the calling procedure.

## What the context does NOT carry

- **No caller identity.** If you need "who is doing this" (for `CreatedBy`, `ModifiedBy`, auditing), declare a `public string CurrentUser { get; set; } = "";` parameter on the procedure and have callers pass it.
- **No implicit transaction.** See above.
- **No change tracker.** This is not EF Core — `Update` writes the entity you pass. No navigation fixup, no entity graph, no `SaveChanges`.

## Handy members

```csharp
string           DatabaseName { get; }   // Current database (e.g. "master")
IServiceProvider Services     { get; }   // Scope's service provider (escape hatch)
```

Reach for `Services` only when you need a one-off dependency you didn't declare in the constructor — it's deliberately awkward so that dependencies stay visible on the class.

## Related

- [Procedures](/fundamentals/procedures/) — how `Execute` gets called in the first place
- [Entities & AutoRepo](/fundamentals/entities/) — what `GetTable<T>` returns
- [Scheduling](/fundamentals/scheduling/) — `ExecuteAsync` and `QueueExecuteAsync` in jobs
- [Back up a database](/how-to/back-up-a-database/) — example of calling a system procedure from another procedure
