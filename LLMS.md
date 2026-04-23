# SmartData — LLM developer reference

Guide for AI coding assistants writing application code on the SmartData .NET framework. Scope: entities, stored procedures, callers, scheduling. Skips framework internals (CLI, admin console, binary protocol, scheduler claim/heartbeat).

## Mental model

- All business logic = **stored procedures** (C# classes, no T-SQL). One class = one named, parameterized unit of work.
- Entities = plain classes with LinqToDB attributes. SmartData auto-creates tables/columns/indexes (`SchemaMode.Auto`, default).
- Single transport: `POST /rpc` (binary). One endpoint, all procedures.
- Two caller surfaces: `IProcedureService` (system authority, no auth) vs `IAuthenticatedProcedureService` (per-user session).

## Program.cs setup

Order matters. `AddSmartDataScheduler()` must come **after** `AddStoredProcedures()`.

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSmartData();                                  // core engine
builder.Services.AddSmartDataSqlite();                            // or AddSmartDataSqlServer(o => ...)
builder.Services.AddStoredProcedures(typeof(Program).Assembly);   // discovery
builder.Services.AddSmartDataScheduler();                         // optional, must be after AddStoredProcedures

var app = builder.Build();
app.UseSmartData();                                               // maps POST /rpc + GET /health
app.Run();
```

Configuration:

```csharp
builder.Services.AddSmartData(o =>
{
    o.SchemaMode = SchemaMode.Auto;          // default; use Manual in production
    o.IncludeExceptionDetails = false;       // true in Dev
    o.Index.Prefix = "SD_";
});
```

> **`SchemaMode.Auto` never drops or renames columns and never migrates data.** Renaming a property creates a new column and orphans the old one. Use `SchemaMode.Manual` in production unless you accept this.

## Entities

LinqToDB mapping + SmartData index attributes. Class-level `[Index]`/`[FullTextIndex]` (stackable).

```csharp
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

[Table]
[Index("IX_Customer_Status", nameof(Status))]
[Index("IX_Customer_Email", nameof(ContactEmail), Unique = true)]
[FullTextIndex(nameof(CompanyName), nameof(ContactName), nameof(Notes))]
public class Customer
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }

    [Column] public string CompanyName { get; set; } = "";
    [Column, MaxLength(256)] public string ContactEmail { get; set; } = "";
    [Column, Nullable] public string? Notes { get; set; }
    [Column] public string Status { get; set; } = "Active";

    [Column] public DateTime CreatedOn { get; set; }
    [Column] public string CreatedBy { get; set; } = "";
    [Column, Nullable] public DateTime? ModifiedOn { get; set; }
    [Column, Nullable] public string? ModifiedBy { get; set; }
}
```

Conventions:

- Non-nullable strings init to `""`. Nullable use `?` + `[Nullable]`.
- No navigation properties — use FK columns (`CustomerId`).
- No inheritance. Flat classes only.
- Audit fields (`CreatedOn`/`CreatedBy`/`ModifiedOn?`/`ModifiedBy?`) — set in procedures, not by framework.

## Stored procedures

Public properties = parameters (case-insensitive name binding). Class name → `usp_snake_case` (`CustomerList` → `usp_customer_list`). Constructor exists for DI; receives `IDatabaseContext` redundantly with `Execute` — leave body empty unless injecting other services.

```csharp
using SmartData.Server.Procedures;

public class CustomerList : StoredProcedure<CustomerListResult>
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;

    public CustomerList(IDatabaseContext ctx) { }

    public override CustomerListResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var query = ctx.GetTable<Customer>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(Search))
            query = query.Where(c => c.CompanyName.Contains(Search.Trim()));

        var total = query.Count();
        var items = query.OrderBy(c => c.CompanyName)
                         .Skip((Page - 1) * PageSize)
                         .Take(PageSize)
                         .ToList();

        return new CustomerListResult
        {
            Items = items.Select(Map).ToList(),
            Total = total, Page = Page, PageSize = PageSize
        };
    }
}
```

Pick base class:

| Base | Override | When |
|------|----------|------|
| `StoredProcedure<TResult>` | `Execute(ctx, ct)` | Sync I/O — the common case |
| `AsyncStoredProcedure<TResult>` | `ExecuteAsync(ctx, ct)` | Genuine async (external HTTP, calling other procedures) |

Return `VoidResult.Instance` for procedures with no payload.

### IQueryable rule

`ctx.GetTable<T>()` returns LinqToDB `ITable<T>` (`IQueryable`). **Compose `.Where`/`.OrderBy`/`.Skip`/`.Take` before `.ToList()`.** Calling `.ToList()` first loads the whole table into memory.

### Errors — RaiseError

```csharp
RaiseError("Customer not found.");                           // simple
RaiseError(1001, "Customer not found.");                     // with id
RaiseError(1002, "Email taken.", ErrorSeverity.Severe);      // with severity
```

- `[DoesNotReturn]` — flow analysis treats post-call code as unreachable; nullable analysis works after a guard.
- Message IDs: `0–999` system, `1000+` user.
- Throws `ProcedureException` — message always returned to caller (other exceptions return generic message unless `IncludeExceptionDetails = true`).
- `ErrorSeverity.Fatal` short-circuits scheduler retry.

### Transactions

Procedures don't run in a transaction by default. Wrap multi-step writes:

```csharp
using var tx = ctx.BeginTransaction();
ctx.Delete<CustomerContact>(x => x.CustomerId == Id);
ctx.Delete(customer);
tx.Commit();   // missing Commit + Dispose = rollback
```

### Calling other procedures

```csharp
var stats = await ctx.ExecuteAsync<DashboardStatsResult>("usp_dashboard_stats");
ctx.QueueExecuteAsync("usp_send_notification", new { UserId = 42 });   // fire-and-forget
```

### Audit fields — caller passes the user

`IDatabaseContext` does not expose caller identity. Declare `public string CurrentUser { get; set; } = "";` on the procedure; have the caller pass it.

| Op | CreatedOn | CreatedBy | ModifiedOn | ModifiedBy |
|----|-----------|-----------|------------|------------|
| Insert | `DateTime.UtcNow` | `CurrentUser` | — | — |
| Update | leave alone | leave alone | `DateTime.UtcNow` | `CurrentUser` |

## Contracts folder layout

`Contracts/<ProcedureName>/` — one folder per procedure, name matches procedure class. Shared CRUD types in `Contracts/Common/` (`SaveResult`, `DeleteResult`).

```
Contracts/
├── Common/{SaveResult.cs, DeleteResult.cs}
├── CustomerList/{CustomerListResult.cs, CustomerItem.cs}
└── CustomerGet/CustomerGetResult.cs
```

DTOs: flat, no methods, init strings to `""`, init lists to `new()`. Binary serializer maps **by property name, case-insensitive** — procedure result type and contract type don't have to be the same class, just matching shapes.

## Calling procedures (in-process)

Inject the right service. Both are scoped — one scope = one caller.

| Service | Auth gate | Use from |
|---------|-----------|----------|
| `IProcedureService` | Bypassed (`UserId="system"`) | Server code: schedulers, startup seeders, internal wiring |
| `IAuthenticatedProcedureService` | Enforced | User-facing controllers (call `Authenticate(token)` once per scope) |

Decision: *does this caller represent a specific end user, or is it the server acting on its own authority?* End user → `IAuthenticatedProcedureService`. Server → `IProcedureService`. Don't reach around the auth gate.

```csharp
public class CustomersController(IProcedureService procedures) : Controller
{
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var result = await procedures.ExecuteAsync<CustomerListResult>(
            "usp_customer_list",
            new { Search = search, Page = page, PageSize = 20 });
        return View(result);
    }

    [HttpPost]
    public async Task<IActionResult> Save(CustomerSaveViewModel m)
    {
        var result = await procedures.ExecuteAsync<SaveResult>(
            "usp_customer_save",
            new { m.Id, m.CompanyName, m.ContactEmail,
                  CurrentUser = User.Identity?.Name ?? "unknown" });
        return RedirectToAction("Detail", new { id = result.Id });
    }
}
```

Target database: each procedure picks via `db.UseDatabase(...)` internally — pass a `Database` arg if the procedure declares one.

## Calling procedures (remote)

`SmartDataConnection` — ADO.NET-shaped client.

```csharp
await using var conn = new SmartDataConnection(
    "Server=http://localhost:5124;User Id=admin;Password=secret");
await conn.OpenAsync();   // performs sp_login, stores token

var response = await conn.SendAsync("usp_customer_list", new Dictionary<string, object>
{
    ["Database"] = "master",
    ["Search"]   = "acme",
    ["Page"]     = 1,
    ["PageSize"] = 20
});

if (response.Success)
    var result = response.GetData<CustomerListResult>();
else
    Console.WriteLine(response.Error);
```

Connection-string keys: `Server`, `User Id` (aliases: `UID`/`Username`/`User`), `Password` (alias: `PWD`), `Token`, `Timeout`. Pass `Token=` instead of credentials to skip the login round-trip. `ConnectionString` getter masks `Password`. `State` = `System.Data.ConnectionState`.

## Scheduling

A scheduled job = a stored procedure with a cadence attribute. **Code owns *when*; users own only `Enabled`/`RetryAttempts`/`RetryIntervalSeconds`/`JitterSeconds` (preserved across reconciles).**

```csharp
using SmartData.Server.Scheduling.Attributes;

[Job("Nightly Cleanup", Category = "Ops")]
[Daily("03:15")]
[Retry(attempts: 3, intervalSeconds: 60)]
public class NightlyCleanup : AsyncStoredProcedure<VoidResult>
{
    public override async Task<VoidResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
    {
        // ...
        return VoidResult.Instance;
    }
}
```

Cadence attributes (server-local time; stackable):

| Attribute | Example |
|-----------|---------|
| `[Daily]` | `[Daily("02:00")]` — `Days = Days.Weekdays` to narrow |
| `[Every]` | `[Every(5, Unit.Minutes)]` — `Between = "09:00-17:00"` for windows |
| `[Weekly]` | `[Weekly(Days.Mon \| Days.Fri, "06:00")]` — `Every = N` for biweekly |
| `[Monthly]` | `[Monthly(Day.D1 \| Day.Last, "00:30")]` — D29/30/31 skip months that lack them |
| `[MonthlyDow]` | `[MonthlyDow(Weeks.First, Days.Mon, "06:00")]` — "first Monday" |
| `[Once]` | `[Once("2026-06-01 09:00")]` — auto-disables after firing |
| `[Job]` | Display metadata only, not persisted |
| `[Retry]` | See below |

> **`[Retry(attempts: 3)]` = 3 total runs (1 initial + 2 retries).** Not "3 retries after the first." `attempts: 1` = no retry. `ErrorSeverity.Fatal` always short-circuits retry.

Catch-up: missed fires are **dropped** by default. `SchedulerOptions.MaxCatchUp = N` queues up to N — only safe for idempotent jobs.

## Common shapes

- **List** — pagination + filters. Compose `IQueryable` then `.ToList()`. Run `Count()` against the filtered query for `Total`.
- **Get** — `FirstOrDefault` + `if (x == null) RaiseError(...)`. Load related lists with separate queries.
- **Save** — nullable `Id`. `Id > 0` = update (don't touch `CreatedOn`/`CreatedBy`); else insert. Always set `ModifiedOn`/`ModifiedBy` on update.
- **Delete** — validate exists, delete dependents first (or wrap in transaction), delete entity.

## Pitfalls

- Forgetting `AddStoredProcedures` before `AddSmartDataScheduler` → scheduler can't see your jobs.
- Calling `.ToList()` then filtering in C# → loads whole table.
- Renaming an entity property under `SchemaMode.Auto` → orphan column, no data migration.
- Using `IAuthenticatedProcedureService` from a scheduler → no session, request fails. Use `IProcedureService` for server-side authority.
- Setting `CreatedOn`/`CreatedBy` on update → audit history lost.
- Mistaking `[Retry(3)]` for "3 retries" → it's 3 total runs.
- Catching `ProcedureException` and rethrowing as a generic exception → loses the message-passthrough behavior.

## Reference docs

- `docs/SmartData.Guide.md` — full developer guide (mental models, end-to-end CRUD, production notes)
- `docs/SmartData.Server.md` — engine + provider reference
- `docs/SmartData.Client.md` — client surface
- `https://smartdata-apis.netlify.app/` — published Astro site (task-oriented)
