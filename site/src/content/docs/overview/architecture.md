---
title: Architecture
description: SmartData at 30,000 feet — projects, layers, and how they fit together.
---

SmartData is a .NET 10 data framework built around three ideas: data lives in auto-migrating [entities](/fundamentals/entities/), business logic lives in typed [stored procedures](/fundamentals/procedures/), and every client–server call is one [binary RPC](/fundamentals/binary-rpc/) to `POST /rpc`. No controllers, no service layers, no per-endpoint routing.

## Project layers

All projects target `net10.0` and every library publishes a NuGet package.

```
         ┌──────────────────────────────────────────────────┐
         │  SmartData.Core        binary serializer +       │
         │                        protocol models           │
         │  SmartData.Contracts   shared provider contracts │
         └──────────────────────────────────────────────────┘
                                 ▲
                                 │
         ┌───────────────────────┴──────────────────────────┐
         │  SmartData.Server      engine: AutoRepo, procs,  │
         │                        scheduler, session,       │
         │                        metrics, backups          │
         │    ├─ SmartData.Server.Sqlite      dev / local   │
         │    └─ SmartData.Server.SqlServer   production    │
         └──────────────────────────────────────────────────┘
                                 ▲
                                 │
         ┌───────────────────────┴──────────────────────────┐
         │  SmartData.Client      HTTP client for /rpc      │
         │  SmartData.Cli         `sd` single-file CLI      │
         │  SmartData.Console     embedded admin UI         │
         └──────────────────────────────────────────────────┘
```

| Project | Role | Reference |
|---|---|---|
| Core | Binary serializer, `CommandRequest` / `CommandResponse`, `VoidResult`. Zero dependencies. | [smartdata-core](/reference/smartdata-core/) |
| Server | AutoRepo ORM, procedure discovery + executor, RPC router, session/auth, metrics, backup, scheduler. | [smartdata-server](/reference/smartdata-server/) |
| Server.Sqlite | SQLite provider — file-based databases, FTS5, WAL journal. | [smartdata-server-sqlite](/reference/smartdata-server-sqlite/) |
| Server.SqlServer | SQL Server provider — full-text via CONTAINS + catalog, IDENTITY columns, native ALTER TABLE. | [smartdata-server-sqlserver](/reference/smartdata-server-sqlserver/) |
| Client | Lightweight HTTP client wrapping binary RPC calls. | [smartdata-client](/reference/smartdata-client/) |
| Cli | Self-contained `sd` executable for database, table, data, backup, import/export operations. | [smartdata-cli](/reference/smartdata-cli/) |
| Console | Embedded MVC admin UI mounted at `/{prefix}/console/` — tables, query builder, users, backups, schedules, metrics. | [smartdata-console](/reference/smartdata-console/) |

## The three big ideas

### Entities → AutoRepo

Entities are plain classes with LinqToDB `[Table]` / `[Column]` / `[PrimaryKey, Identity]` attributes, plus SmartData's stackable `[Index]` / `[FullTextIndex]`. With `SchemaMode.Auto` (default), first use of each entity compares the class to the live database and adds tables, columns, and indexes. Auto mode **never drops or renames columns** — renaming a property creates a new column and leaves the old one in place. Use `SchemaMode.Manual` in production unless you understand that.

See: [Entities](/fundamentals/entities/).

### Procedures own business logic

Business logic lives in classes extending `StoredProcedure<TResult>` (sync) or `AsyncStoredProcedure<TResult>` (async). `services.AddStoredProcedures(assembly)` scans for implementations at startup. Public properties on the class are parameters, bound by name and case-insensitive. Naming convention: `CustomerList` auto-registers as `usp_customer_list`; framework procedures use the `sp_` prefix. Errors use `RaiseError(id, msg, severity)` — IDs `0–999` are system, `1000+` are yours.

See: [Procedures](/fundamentals/procedures/), [Database context](/fundamentals/database-context/).

### One endpoint, binary

All traffic is `POST /rpc` with content type `application/x-binaryrpc`. Request and response bodies are `BinarySerializer`-encoded `CommandRequest` / `CommandResponse`; the `Args` dictionary inside the request is itself binary-serialized (double-serialized). There is no JSON and browser devtools cannot inspect payloads — for debugging, call `CommandResponse.GetDataAsJson()` server-side. `app.UseSmartData()` wires the endpoint plus `GET /health` in one line.

See: [Binary RPC](/fundamentals/binary-rpc/).

## Request lifecycle

```
Client                                    Server
  │                                          │
  │  POST /rpc  (application/x-binaryrpc)    │
  │  BinarySerialize(CommandRequest)         │
  ├─────────────────────────────────────────▶│
  │                                          │  CommandRouter
  │                                          │    ├─ deserialize request
  │                                          │    └─ validate token
  │                                          │          │
  │                                          │          ▼
  │                                          │  ProcedureExecutor
  │                                          │    ├─ resolve from catalog
  │                                          │    ├─ create DI scope
  │                                          │    ├─ instantiate procedure
  │                                          │    ├─ bind params (by name)
  │                                          │    └─ Execute(ctx, ct)
  │                                          │          │
  │                                          │          ▼
  │  BinarySerialize(CommandResponse)        │  serialize result
  │◀─────────────────────────────────────────┤
  │                                          │
```

Every call gets a fresh scoped `IDatabaseContext` with a pooled connection. Procedures do **not** run inside an implicit transaction — use `ctx.BeginTransaction()` + `tx.Commit()` for multi-step atomicity.

Two caller interfaces separated by trust: `IProcedureService` runs under framework authority (`UserId = "system"`, auth gate bypassed) for schedulers and trusted server-side code; `IAuthenticatedProcedureService` enforces session auth and is wired automatically for `/rpc` and user-facing controllers. Don't reach around the auth gate — route unauthenticated work through `IProcedureService`.

## Provider abstraction

A provider implements four interfaces (`DatabaseProvider`, `SchemaProvider`, `SchemaOperations`, `RawDataProvider`). Registration is a single service call — swap SQLite for SQL Server without touching procedures or entities. Multi-database is first-class: the `Database` arg on each RPC selects which logical database the call targets.

See: [Providers](/fundamentals/providers/).

## Built-in subsystems

| Subsystem | What it does | Read more |
|---|---|---|
| Scheduler | `[Daily]` / `[Every]` / `[Weekly]` / `[Monthly]` / `[Once]` attributes on a procedure. Code owns *when*; users own only `Enabled`, retry, and jitter. Multi-instance safe via unique-index claim + heartbeat. | [Scheduling](/fundamentals/scheduling/) |
| Tracking & Ledger | Opt-in `[Tracked]` provisions `{Table}_History` (post-image + op/user/timestamp). `[Ledger]` adds a SHA-256 hash chain for tamper-evident audit. | [Tracking](/fundamentals/tracking/) |
| Session / Auth | PBKDF2 password hashing, token-scoped sessions, RBAC permissions. | [smartdata-server](/reference/smartdata-server/) |
| Metrics | Counters, histograms, and spans collected per-procedure. | [smartdata-server](/reference/smartdata-server/) |
| Backup | Versioned manifest-based backup/restore. | [smartdata-server](/reference/smartdata-server/) |
| Admin UI | Razor/MVC + HTMX + Tailwind. Tables, query builder, users, schedules, metrics, backups. | [smartdata-console](/reference/smartdata-console/) |
| CLI | `sd` single-file executable; config persisted at `~/.sd/config.json`. | [smartdata-cli](/reference/smartdata-cli/) |

## Where to go next

- **Build something** — [Install](/get-started/install/) → [First procedure](/get-started/your-first-procedure/) → [First RPC call](/get-started/your-first-rpc-call/)
- **Mental models** — [Procedures](/fundamentals/procedures/) · [Database context](/fundamentals/database-context/) · [Binary RPC](/fundamentals/binary-rpc/)
- **API surface** — [System procedures](/reference/system-procedures/) · [SmartData.Server reference](/reference/smartdata-server/)
