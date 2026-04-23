# SmartData

**A batteries-included .NET data framework.** One package, one endpoint, one mental model:
write a stored-procedure class, hit `POST /rpc`, and get a typed result back — with schema
migration, change tracking, scheduling, and an embedded admin console thrown in.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue)](LICENSE)
[![Docs](https://img.shields.io/badge/docs-smartdata--apis.netlify.app-0b7cff)](https://smartdata-apis.netlify.app/)
[![NuGet Feed](https://img.shields.io/badge/NuGet-custom%20feed-004880)](https://smartdata-apis.netlify.app/nuget/v3/index.json)

---

## Why SmartData

Most .NET data stacks force you to stitch together an ORM, a web framework, a migration
tool, a background scheduler, an admin panel, and a client SDK. SmartData ships them as
one cohesive set with a single opinion: **business logic lives in stored-procedure classes**,
and everything else — routing, schema, auditing, scheduling — is wired up for you.

- **Everything is a stored procedure.** No controllers, no service layers.
  `class CustomerList : StoredProcedure<Result>` becomes `usp_customer_list` automatically.
- **Auto-migrating ORM.** Declare entities with attributes; AutoRepo adds tables, columns,
  and indexes on first use. No migrations folder to babysit.
- **Single binary RPC endpoint.** One `POST /rpc` route, typed end-to-end, no JSON overhead.
- **Built-in scheduler.** Attribute a procedure with `[Daily]` or `[Every(...)]` and it runs.
  Multi-instance safe, retries, jitter, catch-up policy.
- **Change tracking & ledger.** `[Tracked]` for queryable history, `[Ledger]` for
  hash-chained tamper-evidence — no code changes to your writes.
- **Embedded admin console.** Browse data, inspect schema, manage users, tail logs,
  manage scheduled jobs — all from `/console/`.
- **`sd` CLI.** Global .NET tool for connecting, browsing databases, running procedures
  from the terminal.

---

## Quick start

### 1. Add the feed and install

```bash
dotnet nuget add source https://smartdata-apis.netlify.app/nuget/v3/index.json --name SmartData
dotnet add package SmartData.Server.Sqlite
```

### 2. Wire it up

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
builder.Services.AddSmartDataScheduler();   // optional
builder.Services.AddSmartDataConsole();     // optional admin UI

var app = builder.Build();
app.UseSmartData();         // maps POST /rpc + GET /health
app.UseSmartDataConsole();  // maps /console/
app.Run();
```

### 3. Declare an entity

```csharp
[Table]
public class Customer
{
    [PrimaryKey, Identity, Column] public int    Id          { get; set; }
    [Column]                       public string CompanyName { get; set; } = "";
    [Column]                       public string Status      { get; set; } = "Active";
    [Column]                       public DateTime CreatedOn { get; set; }
}
```

On first use, the table is created. Add a property later — the column is added. No migration
scripts, no CLI dance.

### 4. Write a procedure

```csharp
public class CustomerList : StoredProcedure<CustomerList.Result>
{
    public string? Search   { get; set; }
    public int     Page     { get; set; } = 1;
    public int     PageSize { get; set; } = 20;

    public CustomerList(IDatabaseContext ctx) : base(ctx) { }

    public override Result Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var q = ctx.GetTable<Customer>().Where(c => c.Status == "Active");
        if (!string.IsNullOrWhiteSpace(Search))
            q = q.Where(c => c.CompanyName.Contains(Search));

        return new Result
        {
            Items = q.OrderBy(c => c.CompanyName)
                     .Skip((Page - 1) * PageSize)
                     .Take(PageSize)
                     .ToList(),
            Total = q.Count(),
        };
    }

    public sealed class Result
    {
        public List<Customer> Items { get; set; } = new();
        public int Total { get; set; }
    }
}
```

Class `CustomerList` auto-registers as `usp_customer_list`. Case-insensitive args binding,
case-insensitive naming.

### 5. Call it from a client

```csharp
await using var conn = new SmartDataConnection(
    $"Server=https://your-server;Token={sessionToken}");
await conn.OpenAsync();

var response = await conn.SendAsync("usp_customer_list",
    new Dictionary<string, object> { ["Search"] = "acme", ["Page"] = 1 });

if (response.Success)
    HandleList(response.GetData<CustomerList.Result>());
```

Or from the CLI:

```bash
sd connect https://your-server
sd call usp_customer_list --Search acme --Page 1
```

---

## Architecture

```
┌────────────┐    POST /rpc    ┌──────────────────────────────────────────────┐
│ Client /   │ ──────────────> │  CommandRouter                               │
│ Console /  │ <────────────── │    └─ ProcedureExecutor                      │
│ CLI        │  binary response│         └─ resolves IProcedure from catalog  │
└────────────┘                 │              binds args, checks perms,       │
                               │              runs Execute/ExecuteAsync       │
                               │                                              │
                               │  IDatabaseContext  ──►  linq2db + provider   │
                               │  Scheduler  ──►  reconciles [Daily]/[Every]  │
                               │  BackupService, Metrics, Tracking            │
                               └──────────────────────────────────────────────┘
                                            │
                                            ▼
                            SQLite / SQL Server / (custom provider)
```

### Project layering

| Package                  | Purpose                                                    |
|--------------------------|------------------------------------------------------------|
| `SmartData.Core`         | Binary RPC serialization + shared protocol models          |
| `SmartData.Contracts`    | Shared contracts / provider interfaces                     |
| `SmartData.Client`       | HTTP client for `POST /rpc`                                |
| `SmartData.Server`       | Engine: AutoRepo, procedures, scheduler, sessions, backups |
| `SmartData.Server.Sqlite`| SQLite provider                                            |
| `SmartData.Server.SqlServer` | SQL Server provider                                    |
| `SmartData.Console`      | Embedded admin UI (Razor/MVC)                              |
| `SmartData.Cli`          | `sd` .NET global tool                                      |

---

## Feature tour

<table>
<tr><td width="50%">

**Auto-migrating schema**
```csharp
[Table]
[Index("IX_Customer_Email",  nameof(Email), Unique = true)]
[Index("IX_Customer_Status", nameof(Status))]
[FullTextIndex(nameof(CompanyName), nameof(Notes))]
public class Customer { /* ... */ }
```
Tables, columns, indexes, and full-text indexes are created on startup.
`SchemaMode.Auto` never drops data — renaming a property creates a new column.

</td><td>

**Scheduling**
```csharp
[Daily(Hour: 3, Minute: 0)]
[Retry(attempts: 3, intervalSeconds: 60)]
public class NightlyCleanup : AsyncStoredProcedure<VoidResult>
{
    public override async Task<VoidResult> ExecuteAsync(...)
    { /* ... */ }
}
```
Code owns the cadence via attributes; users control only `Enabled` /
retry / jitter via the admin console.

</td></tr>
<tr><td>

**Change tracking**
```csharp
[Table]
[Tracked]                  // queryable history
public class Product { /* ... */ }

[Table]
[Ledger]                   // + hash-chained tamper-evidence
public class Invoice { /* ... */ }
```
Writes automatically mirror to `Product_History` / `Invoice_Ledger`.
Verify integrity with `sp_ledger_verify` + external anchors.

</td><td>

**Errors with IDs**
```csharp
if (balance < 0)
    RaiseError(1042, "Insufficient funds.", ErrorSeverity.Error);
```
`ProcedureException` messages + IDs flow back to clients over the wire —
no string-matching needed on the caller side.

</td></tr>
</table>

---

## Documentation

**Full site:** <https://smartdata-apis.netlify.app/>

Essential reads:
- [Developer guide](docs/SmartData.Guide.md) — mental model, patterns, pitfalls
- [Your first procedure](https://smartdata-apis.netlify.app/get-started/your-first-procedure/)
- [Fundamentals](https://smartdata-apis.netlify.app/fundamentals/procedures/) — procedures, entities, database-context, binary-rpc, scheduling, tracking
- [How-to guides](https://smartdata-apis.netlify.app/how-to/define-a-procedure/) — 11 single-question recipes
- [Tutorials](https://smartdata-apis.netlify.app/tutorials/build-a-crud-app/) — end-to-end CRUD app, migrate an existing schema
- [Reference](https://smartdata-apis.netlify.app/reference/smartdata-server/) — API surface per package + full `sp_*` catalog

---

## Building from source

```bash
# Restore + build the full solution
dotnet build SmartData.slnx

# Pack NuGet packages to ./artifacts/
dotnet pack SmartData.slnx -c Release

# Run the docs site locally
cd site && npm install && npm run dev
```

Requires **.NET 10 SDK**.

---

## Status

SmartData is actively developed. The API surface is stable enough for production use in
trusted-network scenarios; the framework is used to ship the admin console itself. Feedback
and issue reports are welcome.

---

## License

MIT — see [LICENSE](LICENSE).
