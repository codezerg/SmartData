# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build / Run Commands

The repo is a .NET 10 solution (`SmartData.slnx`) plus an Astro docs site under `site/`.

```bash
# Restore + build the whole solution
dotnet build SmartData.slnx

# Build a single project
dotnet build src/SmartData.Server/SmartData.Server.csproj

# Pack NuGet packages (every library project has GeneratePackageOnBuild=true)
dotnet pack SmartData.slnx -c Release
# Output lands in ./artifacts/ (set by Directory.Build.props)

# CLI — published as single-file self-contained `sd` executable
dotnet run --project src/SmartData.Cli -- <args>
dotnet publish src/SmartData.Cli -c Release

# Docs site (Astro Starlight)
cd site && npm install && npm run dev      # dev server
cd site && npm run build                   # static build
```

There is **no test project in the solution** — do not invent `dotnet test` instructions. Testing strategy is left to the consuming app (see `docs/SmartData.Guide.md` → *What This Guide Doesn't Cover*).

## Architecture

SmartData is a .NET data framework. The project layering (all `net10.0`):

```
SmartData.Core          Binary RPC serialization + shared protocol models (no deps)
SmartData.Contracts     Shared contracts / provider interfaces (no deps)
SmartData.Client        ADO.NET-style `SmartDataConnection` over POST /rpc  → depends on Core + Contracts
SmartData.Server        Engine: AutoRepo ORM, stored procedure framework, scheduler,
                        session, metrics, backups  → depends on Core + Contracts, linq2db 5.4
SmartData.Server.Sqlite       SQLite provider  → depends on Server
SmartData.Server.SqlServer    SQL Server provider  → depends on Server
SmartData.Console       Embedded admin UI (Razor/MVC)  → depends on Server
SmartData.Cli           `sd` CLI, single-file self-contained  → depends on Client
```

### Core concepts (see `docs/SmartData.Guide.md` for the full picture)

- **Everything is a stored procedure.** Business logic lives in classes extending `StoredProcedure<TResult>` or `AsyncStoredProcedure<TResult>`. There are no controllers or service layers between callers and procedures. Public properties on the class are parameters bound by name (case-insensitive). Class `CustomerList` is auto-registered as `usp_customer_list`. System procedures use the `sp_` prefix; user procedures use `usp_`.
- **Auto-discovery.** `services.AddStoredProcedures(assembly)` scans for `IStoredProcedure` / `IAsyncStoredProcedure` implementations. Scheduler registration (`AddSmartDataScheduler()`) **must come after** `AddStoredProcedures`.
- **Single RPC endpoint.** `app.UseSmartData()` maps `POST /rpc` (binary protocol, `application/x-binaryrpc`) and `GET /health`. Request/response bodies are `BinarySerializer`-encoded `CommandRequest` / `CommandResponse`; `Args` inside the request is itself binary-serialized (double-serialized dictionary). No JSON; devtools cannot inspect payloads — use `CommandResponse.GetDataAsJson()` server-side for debugging.
- **Two caller interfaces, separated by trust.** `IProcedureService` runs under framework authority (`UserId = "system"`, auth gate bypassed) — for schedulers, startup tasks, trusted server-side code. `IAuthenticatedProcedureService` enforces session auth — wired automatically for `/rpc` and for user-facing controllers. Do not try to reach around the auth gate; route unauthenticated work through `IProcedureService`.
- **ORM: AutoRepo.** Entities are plain classes with LinqToDB `[Table]`/`[Column]`/`[PrimaryKey,Identity]` attributes plus SmartData's `[Index]` / `[FullTextIndex]` (from `SmartData.Server.Attributes`, applied to the class, stackable). With `SchemaMode.Auto` (default), first use of each entity compares the class to the DB and adds tables/columns/indexes. Auto mode **never drops or renames columns or migrates data** — renaming a property creates a new column and leaves the old one. Orphan `NOT NULL` columns (in DB, not on entity) are relaxed to `NULLABLE` on next reconcile so inserts keep working (no data is dropped); disable via `SmartDataOptions.RelaxOrphanNotNull = false`. Use `SchemaMode.Manual` in production unless you understand this.
- **Database context is per-procedure.** Procedures receive `IDatabaseContext ctx` both via constructor DI (needed for DI resolution) and as a parameter to `Execute`/`ExecuteAsync`. `ctx.GetTable<T>()` returns LinqToDB `ITable<T>` (`IQueryable`) — **compose `.Where`/`.OrderBy`/`.Skip`/`.Take` before `.ToList()`** so filtering pushes to SQL. Procedures do **not** run inside a transaction; use `ctx.BeginTransaction()` + `using` + `tx.Commit()` for multi-step atomicity. `ctx.ExecuteAsync<T>(spName, args)` calls another procedure; `ctx.QueueExecuteAsync(...)` is fire-and-forget background.
- **Errors.** `RaiseError(msg)` / `RaiseError(id, msg, severity)` throws `ProcedureException`. It is `[DoesNotReturn]`, so nullable flow analysis works. Message IDs: `0–999` system, `1000+` user. `ProcedureException` messages are always returned to the caller; other exceptions return a generic message unless `options.IncludeExceptionDetails = true`.
- **Scheduler.** A scheduled job is a stored procedure with a `[Daily]`/`[Every]`/`[Weekly]`/`[Monthly]`/`[MonthlyDow]`/`[Once]` attribute. Code owns *when* (attributes are overwritten into `_sys_schedules` on every startup reconcile). Users own only `Enabled`, `RetryAttempts`, `RetryIntervalSeconds`, `JitterSeconds` (preserved across reconciles). `[Retry(attempts: 3)]` = 3 **total** runs (1 initial + 2 retries), not 3 retries. `ErrorSeverity.Fatal` short-circuits retry. Multi-instance safe (unique-index claim + heartbeat). Default catch-up policy drops missed fires; `SchedulerOptions.MaxCatchUp` queues up to N — only enable for idempotent jobs.
- **Audit fields.** Convention is `CreatedOn`/`CreatedBy`/`ModifiedOn?`/`ModifiedBy?`. Not enforced by the framework — procedures set them. `IDatabaseContext` does not expose the caller identity; pass a `CurrentUser` public parameter on the procedure and have the caller provide it. This keeps procedures decoupled from ASP.NET.
- **Client is ADO.NET-shaped.** `SmartDataConnection` takes a connection string (`Server=...;User Id=...;Password=...;Token=...;Timeout=...`), parsed by `SmartDataConnectionStringBuilder` (aliases: `UID`/`Username`/`User`, `PWD`). Usage: construct → `OpenAsync` (logs in if creds given, otherwise uses `Token`) → `SendAsync(command, args)` → `DisposeAsync`. `State` exposes `ConnectionState`; `ConnectionString` getter masks `Password`.
- **Admin surface.** `SmartData.Console` mounts a Razor admin UI at `/console` (configurable via `ConsoleOptions.RoutePrefix`) behind `UseSmartDataConsole()`. The `sd` CLI is a thin wrapper over `SmartDataConnection` — commands are noun/verb (`db`, `table`, `column`, `index`, `sp`, `user`, `backup`, `settings`, `exec`, `dump`, `connect`, `login`, `logs`, `metrics`, `data`, `storage`) and all dispatch to `sp_*` system procedures via `/rpc`. Adding a system procedure auto-exposes it; adding a CLI verb is just wiring in `src/SmartData.Cli/Commands/`.

### Contracts folder convention

DTOs live under `Contracts/<ProcedureName>/`, one folder per procedure, matching the procedure class name. Shared CRUD result types (`SaveResult`, `DeleteResult`) go in `Contracts/Common/`. The binary serializer maps **by property name, case-insensitive** — the procedure's `TResult` type and the contract DTO don't have to be the same type, just matching shapes.

## Documentation

Two parallel doc sets; know which to read and which to edit.

**`docs/*.md` (repo root)** — long-form design pool. Consult these first for non-trivial changes to a given project. Not rendered by the site; they exist as the source-of-truth technical pool.

- `docs/SmartData.Guide.md` — developer guide (read this first for anything procedure/entity-shaped)
- `docs/SmartData.Core.md` · `docs/SmartData.Server.md` · `docs/SmartData.Server.Sqlite.md` · `docs/SmartData.Server.SqlServer.md`
- `docs/SmartData.Server.Tracking.md` · `docs/SmartData.Console.md` · `docs/SmartData.Client.md` · `docs/SmartData.Cli.md`

**`site/src/content/docs/` (Astro Starlight)** — published site at https://smartdata-apis.netlify.app/. Task-oriented IA, heavy cross-linking, decomposed from the pool (not copied verbatim). ~34 pages in seven sections:

- `overview/` — architecture at 30k ft
- `get-started/` — install, first procedure, first RPC call
- `fundamentals/` — procedures, entities, database-context, binary-rpc, providers, scheduling, tracking (mental models, explained once)
- `how-to/` — 11 single-question recipes with paste-ready snippets
- `tutorials/` — build-a-crud-app, migrate-an-existing-schema (end-to-end)
- `reference/` — one thin page per project + `system-procedures.md` (full `sp_*` catalog grouped by concern)
- `samples/` — pointer page (no `samples/` dir in the repo yet)

When editing site docs: reference pages are **API surface only** — types, signatures, options, one-line descriptions. Rationale/mental-model content belongs in `fundamentals/`, not reference. Every how-to answers one question. Cross-link liberally.
