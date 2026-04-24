---
title: Procedures
description: The stored-procedure pattern — classes, parameters, lifecycle, errors.
---

All business logic in a SmartData app lives in **procedure classes**. There are no controllers, services, or data access layers in between a caller and a procedure — the framework discovers each class, registers it by a derived name, binds the caller's arguments to its public properties, and invokes it.

The term *stored procedure* is borrowed from SQL. These are plain C# — there is no T-SQL. What's shared with a database stored procedure is the shape: a named, parameterized, typed unit of work, called by name.

## Anatomy

```csharp
using MyApp.Contracts.CustomerList;
using SmartData.Server.Procedures;

namespace MyApp.Procedures;

public class CustomerList : StoredProcedure<CustomerListResult>
{
    // Parameters — public properties, bound by name (case-insensitive)
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    // Constructor — required for DI resolution. Leave empty if no extra deps.
    public CustomerList(IDatabaseContext ctx) { }

    // Work — returns the typed result
    public override CustomerListResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        // ...
        return new CustomerListResult { /* ... */ };
    }
}
```

Four moving parts:

1. **Base class** — `StoredProcedure<TResult>` or `AsyncStoredProcedure<TResult>`. Pick async only when you have genuine async I/O (calling other procedures, external HTTP). Sync is simpler for pure database work; see [Database context](/fundamentals/database-context/) for when to pick which.
2. **Parameters** — public properties. Defaults work: `public int Page { get; set; } = 1;`.
3. **Constructor** — used by DI to instantiate. Add parameters for any service you want injected (`ILogger<T>`, a domain service, etc.). The `IDatabaseContext` parameter looks redundant since `Execute` also receives `ctx` — it exists solely because DI needs *some* constructor to resolve against.
4. **`Execute` / `ExecuteAsync`** — business logic. Gets a fresh `IDatabaseContext` and a `CancellationToken`.

## Naming

Class names are converted to `usp_snake_case` at registration:

| Class | Procedure name |
| --- | --- |
| `CustomerList` | `usp_customer_list` |
| `CustomerGet` | `usp_customer_get` |
| `ContactDelete` | `usp_contact_delete` |
| `DashboardStats` | `usp_dashboard_stats` |

- `usp_` — **u**ser **s**tored **p**rocedure (your code)
- `sp_` — framework system procedure (scheduler, backups, schema — see [System procedures](/reference/system-procedures/))

## Registration

Auto-discovery at startup:

```csharp
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
```

Scans the assembly for every `IStoredProcedure` / `IAsyncStoredProcedure` and wires it into the catalog. To register with a custom name:

```csharp
builder.Services.AddStoredProcedure<MyProcedure>("custom_name");
```

> **Ordering matters:** if you call `AddSmartDataScheduler()`, it must come **after** `AddStoredProcedures` — the scheduler reconciler reads the catalog at startup.

## How a call flows

A single procedure call, from either a local caller or `POST /rpc`:

1. Caller names a procedure and supplies args (`new { Search = "acme", Page = 2 }`).
2. The framework opens a DI scope (one per call).
3. The procedure class is instantiated via `ActivatorUtilities.CreateInstance`.
4. Public properties are bound from the args by name, case-insensitive. Missing args keep their default values.
5. For authenticated callers, the session + permissions are checked.
6. `Execute` / `ExecuteAsync` runs. The returned object is serialized back to the caller.
7. Scope disposes; connection returns to the pool.

The whole cycle is one DI scope — `IDatabaseContext` and any scoped services are fresh every call.

## Two callers, one boundary

SmartData registers two procedure services, separated by trust:

| Service | Authority | Auth gate | Used from |
| --- | --- | --- | --- |
| `IProcedureService` | Framework (full admin) | **Bypassed.** `UserId = "system"`. | Schedulers, startup tasks, trusted server-side code |
| `IAuthenticatedProcedureService` | Per-user session | **Enforced.** Unauthenticated calls rejected. | `/rpc` (wired automatically), your authenticated controllers, the admin console |

Pick by asking *"does this caller represent a specific end user, or is it the server acting on its own authority?"*. The `/rpc` endpoint is wired to the authenticated variant automatically; there's no way to route a user request through the system service, which is the point.

See [Call procedures from a client](/how-to/call-procedures-from-a-client/) for the remote caller.

## Errors

Use `RaiseError` to throw a `ProcedureException` whose message *is* returned to the caller:

```csharp
var c = ctx.GetTable<Customer>().FirstOrDefault(x => x.Id == Id);
if (c == null) RaiseError($"Customer {Id} not found.");

// Safe to dereference c here — RaiseError is [DoesNotReturn]
return new CustomerGetResult { Id = c.Id, /* ... */ };
```

Signatures:

```csharp
RaiseError("Customer not found.");                   // severity defaults to Error
RaiseError(1001, "Customer not found.");             // user message id
RaiseError(1002, "Email in use.", ErrorSeverity.Severe);
```

**Message IDs:** `0–999` reserved for system, `1000+` for user code. `0` = no specific id.

**Severity:** `Error` (normal failure), `Severe` (data integrity / unexpected), `Fatal` (short-circuits any scheduled retry). All three halt execution — severity is a hint to the caller about how to handle it.

Only `ProcedureException` messages reach the caller. Every other exception returns a generic "internal error" unless you opt in with `options.IncludeExceptionDetails = true` (development only — leaking stack traces in production is a bad idea).

Both `ErrorId` and `ErrorSeverity` cross the RPC boundary (see [Binary RPC](/fundamentals/binary-rpc/)), so remote clients can react programmatically without parsing message strings.

## Common shapes

The four recurring procedure shapes — list, get, save, delete — have recipes with paste-ready examples:

- [Define a procedure](/how-to/define-a-procedure/)
- [Add a new entity](/how-to/add-a-new-entity/)
- [Return DTOs, not entities](/how-to/return-dtos-not-entities/)
- [Schedule a recurring job](/how-to/schedule-a-recurring-job/)

## Related

- [Entities & AutoRepo](/fundamentals/entities/) — what procedures read and write
- [Database context](/fundamentals/database-context/) — the API you use inside `Execute`
- [Scheduling](/fundamentals/scheduling/) — attributes that turn a procedure into a recurring job
- [SmartData.Server reference](/reference/smartdata-server/) — full server surface
