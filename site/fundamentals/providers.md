---
title: Providers
description: How the database provider plugs into the server engine.
---

SmartData.Server is provider-agnostic. The engine — AutoRepo, procedure executor, scheduler, backups — talks to a small set of interfaces. Each database gets its own package that implements them:

- **SmartData.Server.Sqlite** — SQLite (Microsoft.Data.Sqlite). Fast, zero-setup, files on disk.
- **SmartData.Server.SqlServer** — SQL Server (Microsoft.Data.SqlClient). Production-grade.
- **Your own** — implement the interfaces, ship a package, register it.

## Registration

One call in `Program.cs`, after `AddSmartData`:

```csharp
builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();                 // pick one
// builder.Services.AddSmartDataSqlServer(o =>
// {
//     o.ConnectionString = "Server=...;Database=...;Trusted_Connection=true";
// });
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
```

The provider registration is the only database-specific line in a SmartData app. Entities, procedures, contracts stay identical across providers.

See [Register a provider](/how-to/register-a-provider/) for the step-by-step with both providers.

## What a provider provides

Four interfaces make up the contract:

| Interface | Responsibility |
| --- | --- |
| **Database manager** | Create / drop / list databases, verify connection, report version |
| **Schema inspector** | Enumerate tables, columns, indexes; compare against entity classes |
| **Schema mutator** | Create tables, add columns, create/drop indexes, create full-text indexes |
| **Context factory** | Build a LinqToDB `DataConnection` bound to a specific database |

AutoRepo calls the inspector + mutator during the first-use check per entity. The procedure executor uses the context factory for every call. Everything else — serialization, scheduling, session, metrics — is provider-free.

Each provider handles the SQL dialect, native libraries (SQLite needs them for full-text), connection-string parsing, and whatever idiosyncrasies that engine has.

## Trade-offs between the bundled providers

| | SQLite | SQL Server |
| --- | --- | --- |
| Setup | None — just a file path | Connection string to a running server |
| Concurrency | Single-writer, many readers | Full MVCC |
| Full-text | FTS5 virtual table | `CONTAINS` with full-text catalog |
| Schema operations | Mostly `ALTER TABLE`; some rebuilds required | Native `ALTER TABLE` for nearly everything |
| Production fit | Embedded apps, single-node tools, dev/test | Multi-node, high-concurrency, enterprise |
| Default database location | `data/{name}.db` | Configured via connection string |

Pick SQLite for local dev, single-node server apps, and tools. Pick SQL Server (or your own provider on a comparable engine) for multi-node production.

## Multi-database

SmartData hosts multiple logical databases behind one server. The target database travels on every call — either as `CommandRequest.Database` over RPC or as an argument that a procedure binds to `db.UseDatabase(...)`. One deployment can host `master`, `tenant_a`, `tenant_b`, and switch per-call.

Both bundled providers support this. For SQLite it creates a new file per database; for SQL Server it switches catalog.

## Writing your own provider

If you need Postgres, MySQL, DuckDB, or something bespoke: implement the four provider interfaces, publish a package, and ship an `AddSmartData<Your>` extension. See [Write a custom provider](/how-to/write-a-custom-provider/) for the concrete steps and what LinqToDB gives you for free.

## Related

- [Register a provider](/how-to/register-a-provider/) — the one-liner per database
- [Write a custom provider](/how-to/write-a-custom-provider/) — implementing the four interfaces
- [SmartData.Server.Sqlite reference](/reference/smartdata-server-sqlite/)
- [SmartData.Server.SqlServer reference](/reference/smartdata-server-sqlserver/)
