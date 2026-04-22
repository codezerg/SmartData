---
title: SmartData.Server
description: Server engine — AutoRepo ORM, stored procedures, binary RPC, scheduler, backups.
---

Server engine for the SmartData framework. Provides AutoRepo ORM with automatic schema migration, a stored procedure framework with binary RPC protocol, pluggable database provider support, session/auth, background execution, and provider-agnostic backup/restore. Built on .NET 10 with linq2db. Database providers (SQLite, SQL Server, etc.) are separate packages.

## Project Structure

```
SmartData.Server/
├── Api/
│   └── CommandRouter.cs         Routes binary RPC requests to procedures
├── Backup/
│   ├── BackupService.cs         Provider-agnostic backup: async job submission, sidecar manifests, per-file history, retention cleanup
│   ├── BackupJobRunner.cs       BackupJobQueue (Channel<BackupJob>) + BackupJobRunner (BackgroundService) — sequential job execution
│   ├── BackupJob.cs             Internal job state model (progress, status, cancellation)
│   ├── BackupOptions.cs         Retention settings: MaxBackupAge, MaxBackupCount, MaxHistoryAge, MaxHistoryCount
│   ├── BackupManifest.cs        Manifest + schema models (BackupSchemaDefinition, BackupTableDefinition, etc.)
│   └── BackupHistoryEntry.cs    History entry model
├── Providers/
│   ├── IDatabaseProvider.cs     Connection management, database lifecycle, DataDirectory
│   ├── ISchemaProvider.cs       Read-only schema metadata (tables, columns, indexes)
│   ├── ISchemaOperations.cs     DDL execution, type mapping (forward + reverse)
│   ├── IRawDataProvider.cs      Dynamic CRUD (select, insert, update, delete, import, OpenReader)
│   ├── SchemaMode.cs            Auto (SmartData manages schema) vs Manual (you manage schema)
│   └── ProviderModels.cs        Model types for provider interfaces
├── Attributes/
│   ├── IndexAttribute.cs        [Index] attribute for declaring indexes on entities
│   └── FullTextIndexAttribute.cs [FullTextIndex] attribute for declaring FTS indexes
├── AutoRepo/
│   ├── EntityMapping.cs         linq2db mapping from [Table]/[Column] attributes
│   ├── IndexMapping.cs          Reads [Index]/[FullTextIndex] attributes, produces IndexDefinitions
│   ├── IdentityProperty.cs      Auto-increment PK support
│   └── Migration/
│       ├── SchemaInspector.cs    Schema inspection via ISchemaProvider
│       ├── SchemaManager.cs     Compares entity to table, applies migrations
│       ├── SchemaMigrator.cs    Executes DDL via ISchemaOperations
│       └── TableColumn.cs       Column metadata value type
├── Engine/
│   ├── DatabaseManager.cs       Database lifecycle (delegates to IDatabaseProvider)
│   ├── SessionManager.cs        Token-based auth (PBKDF2 passwords, random tokens, expiration)
│   ├── SettingsService.cs       Persistent settings: load from DB, in-memory update, DB save
│   ├── SessionCleanupService.cs Hosted service that purges expired sessions
│   ├── KeyValueStore.cs         In-memory concurrent dictionary store
│   ├── ProcedureCatalog.cs      Registry of stored procedures (name -> type)
│   ├── ProcedureExecutor.cs     Instantiates and executes procedures
│   ├── DatabaseContext.cs       IDatabaseContext implementation (IDisposable, manages pooled connections)
│   ├── InternalDbAccess.cs      Raw SQL execution via linq2db
│   ├── QueryFilterBuilder.cs    JSON filter -> parameterized WHERE clause
│   ├── BackgroundSpQueue.cs     Async channel queue for background work
│   └── BackgroundSpService.cs   Hosted service processing background queue
├── Procedures/
│   ├── IStoredProcedure.cs      Core interfaces: IStoredProcedure (sync) + IAsyncStoredProcedure
│   ├── StoredProcedure.cs       Base classes: StoredProcedure<T>, AsyncStoredProcedure<T>, and (internal) SystemStoredProcedure<T>, SystemAsyncStoredProcedure<T>
│   ├── IDatabaseContext.cs      Context interface (table access, CRUD, exec, DatabaseName, Services)
│   └── IKeyValueStore.cs        Key-value store interface
├── RequestIdentity.cs       Internal scoped service — carries Session/Token/UserId for system procedures
├── Entities/
│   ├── SysUser.cs               _sys_users table (Id, Username, PasswordHash, IsAdmin)
│   ├── SysUserPermission.cs     _sys_user_permissions table (UserId, PermissionKey)
│   ├── SysSetting.cs            _sys_settings table (Key, Value, ModifiedAt)
│   ├── SysLog.cs                _sys_logs table (Type, ProcedureName, Message)
│   ├── SysMetric.cs             _sys_metrics table (rolling daily metrics DB)
│   ├── SysSpan.cs               _sys_spans table (trace span data)
│   ├── SysException.cs          _sys_exceptions table (captured exceptions)
│   ├── SysSchedule.cs           _sys_schedules table (one row per timing rule)
│   └── SysScheduleRun.cs        _sys_schedule_runs table (one row per fire — audit + concurrency lock)
├── Scheduling/
│   ├── Attributes/              Schedule triggers — [Daily]/[Every]/[Weekly]/[Monthly]/[MonthlyDow]/[Once] + [Job] + [Retry]
│   ├── ScheduleEnums.cs         Days/Months/Weeks/Day/Unit flag enums
│   ├── SlotComputer.cs          Pure function: SysSchedule + anchor → next DateTime?
│   ├── ScheduleReconciler.cs    Startup sweep — materializes attributes into _sys_schedules, applies user-modified rules
│   ├── ScheduleReconciliationHostedService.cs    IHostedService that runs reconciler in StartAsync
│   ├── JobScheduler.cs          BackgroundService — pumps sp_scheduler_tick on a timer
│   ├── SchedulerOptions.cs      PollInterval, MaxConcurrentRuns, HistoryRetentionDays, HeartbeatInterval, OrphanTimeout, MaxCatchUp, InstanceId
│   └── SchedulerServiceCollectionExtensions.cs   AddSmartDataScheduler() extension
├── Metrics/
│   ├── MetricsCollector.cs      Singleton registry for instruments, spans, exceptions
│   ├── Counter.cs               Monotonically increasing counter with tags
│   ├── Histogram.cs             Distribution tracking (lock-free, reservoir sampling)
│   ├── Gauge.cs                 Point-in-time values (active connections, queue depth)
│   ├── Span.cs                  IDisposable span with AsyncLocal parent tracking
│   ├── TagSet.cs                Immutable sorted tag tuples for metric dimensions
│   ├── MetricsContext.cs        AsyncLocal procedure name for child components
│   ├── MetricsOptions.cs        Configuration (enabled, sample rate, flush interval, etc.)
│   ├── MetricSnapshot.cs        Data classes for snapshots and flush data
│   ├── RingBuffer.cs            Thread-safe fixed-capacity buffer for spans/exceptions
│   ├── ExceptionRecord.cs       Structured exception POCO with trace linking
│   ├── SqlTrackingInterceptor.cs  linq2db interceptor for automatic SQL instrumentation
│   └── MetricsFlushService.cs   BackgroundService that flushes to rolling daily DBs
├── SystemProcedures/            63 built-in procedures (see below), incl. Scheduling/ subfolder
├── ServiceCollectionExtensions.cs   AddSmartData() + AddStoredProcedures()
├── WebApplicationExtensions.cs      UseSmartData() — maps /rpc + /health endpoints
├── SmartDataOptions.cs              Configuration (SchemaMode, IncludeExceptionDetails, Session, Metrics, Backup, Index)
├── SessionOptions.cs                Session TTL, sliding expiration, cleanup interval
└── SmartDataHealthCheck.cs          IHealthCheck — DB connectivity, uptime, active sessions
```

## ASP.NET Integration

### Registration

```csharp
// Program.cs
builder.Services.AddSmartData();                // core engine (SchemaMode.Auto by default)
builder.Services.AddSmartDataSqlite();          // SQLite database provider
builder.Services.AddStoredProcedures(typeof(Program).Assembly);

var app = builder.Build();
app.UseSmartData();  // maps POST /rpc + GET /health endpoints
```

`AddSmartData()` registers all core singletons: `DatabaseManager`, `SessionManager`, `KeyValueStore`, `ProcedureCatalog`, `ProcedureExecutor`, `CommandRouter`, `BackgroundSpQueue`, `BackgroundSpService`, `BackupService`, `BackupJobQueue`, `BackupJobRunner`, `SessionCleanupService`. Also registers ASP.NET Core health checks (`SmartDataHealthCheck`). Accepts optional `SmartDataOptions` (e.g. `SchemaMode`, `IncludeExceptionDetails`, `Session`, `Backup` retention settings). `IncludeExceptionDetails` defaults to `true` in Development, `false` otherwise — controls whether RPC error responses include `ex.Message` or a generic message (details are always logged server-side).

A database provider must be registered separately (e.g. `AddSmartDataSqlite()` from `SmartData.Server.Sqlite`). The provider registers implementations of `IDatabaseProvider`, `ISchemaProvider`, `ISchemaOperations`, and `IRawDataProvider`.

### Procedure Callers

`AddSmartData()` registers two scoped procedure callers on separate trust tiers:

| Service | Authority | Auth gate | Audit user |
|---------|-----------|-----------|-----------|
| `IProcedureService` | Framework (trusted) | Bypassed | `"system"` |
| `IAuthenticatedProcedureService` | Per-user session (token via `Authenticate`) | Unauthenticated calls rejected (unless the procedure is a framework-internal `[AllowAnonymous]` one); per-permission checks are imperative inside system procedures via `RequestIdentity.Require*` | Session's `UserId` |

Use `IProcedureService` only from trusted server-side code (schedulers, startup tasks, internal wiring). Use `IAuthenticatedProcedureService` from anything that represents an end user — the `/rpc` entry point and the embedded admin console already do.

### Database Provider Abstraction

SmartData.Server defines four interfaces that abstract all database-specific operations:

| Interface | Purpose |
|-----------|---------|
| `IDatabaseProvider` | Connection strings, database lifecycle (create/drop/list), `DataDirectory`, provider-specific initialization |
| `ISchemaProvider` | Read-only schema metadata (tables, columns, indexes, row counts). Includes `GetTableSchema()` for batched single-connection retrieval |
| `ISchemaOperations` | DDL execution, type mapping (forward via `MapType`, reverse via `MapTypeReverse`), default values, full-text index management |
| `IRawDataProvider` | Dynamic CRUD, data import/export, raw SQL execution, streaming `OpenReader` |

Backup operations are handled by the provider-agnostic `BackupService` (see below), not by a provider interface.

Available providers:
- **SmartData.Server.Sqlite** — SQLite provider (file-based .db databases, WAL mode)
- **SmartData.Server.SqlServer** — SQL Server provider (future)

### Schema Mode

Controls whether AutoRepo automatically migrates schema on entity access.

```csharp
builder.Services.AddSmartData(o => o.SchemaMode = SchemaMode.Auto);    // default
builder.Services.AddSmartData(o => o.SchemaMode = SchemaMode.Manual);  // DBA manages schema
```

- **Auto** — SmartData compares entity definitions to the database on first use. Creates tables, adds columns, alters types. Uses `ISchemaProvider.GetTableSchema()` to fetch table existence, columns, and indexes in a single connection.
- **Manual** — No automatic migration. Entity classes must match the database. System procedures (SpTableCreate, etc.) still work for explicit operations.

`AddStoredProcedures(assembly)` scans for `IStoredProcedure` implementations. PascalCase class names convert to snake_case and are prefixed based on the owning assembly: `sp_` for the built-in SmartData.Server system assembly, `usp_` for every other (user) assembly. Example: `CustomerList` registered from `SmartApp.Backend` becomes `usp_customer_list`; `SpTableCreate` defined inside SmartData.Server becomes `sp_table_create` (a leading `Sp` on the class name is stripped before the prefix is applied, so the prefix never doubles up).

`UseSmartData()` maps `POST /rpc` — reads binary `CommandRequest`, routes through `CommandRouter`, returns binary `CommandResponse`. Ensures master database exists on startup.

## RPC Execution Flow

1. Client sends `POST /rpc` with binary-serialized `CommandRequest` (token, command, database, args)
2. `CommandRouter` deserializes args, resolves `IAuthenticatedProcedureService` from a fresh scope, and calls it with the database + token. On failure it returns exception details only if `IncludeExceptionDetails` is enabled; otherwise a generic error message.
3. `IAuthenticatedProcedureService` delegates to `ProcedureExecutor`, which creates an inner DI scope, initializes the scoped `RequestIdentity` (session + token), resolves `IDatabaseContext`, sets the active database, instantiates the procedure, maps parameters to properties (case-insensitive), enforces the anonymous-access gate (rejects unauthenticated calls unless the procedure is internally marked `[AllowAnonymous]`), and calls `Execute(ctx, ct)` / `ExecuteAsync(ctx, ct)`. Per-permission checks are imperative inside system procedures via `RequestIdentity.Require*`. All connections are disposed when the scope ends.
4. Result serialized to binary `CommandResponse` and returned

## Stored Procedure Framework

### Defining a Procedure

```csharp
public class CustomerList : StoredProcedure<CustomerListResult>
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;

    public override Task<CustomerListResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
    {
        var customers = ctx.GetTable<Customer>();
        // ... business logic using ITable<T> (IQueryable) ...
        return Task.FromResult(new CustomerListResult { Items = items, Total = total });
    }
}
```

- Extend `StoredProcedure<TResult>` for sync procedures (`public override TResult Execute(IDatabaseContext ctx, CancellationToken ct)`) or `AsyncStoredProcedure<TResult>` for async (`public override Task<TResult> ExecuteAsync(...)`)
- Public properties become parameters (bound by name, case-insensitive)
- Constructor injection supported (including `IDatabaseContext`)
- Auto-discovered by assembly scanning
- All user procedures require authentication — there is no declarative opt-out. `[AllowAnonymous]` is framework-internal and applied only to a small set of system procedures (e.g. `sp_login`). For trusted server-side code (startup tasks, schedulers) use `IProcedureService`, which bypasses the auth gate entirely.
- Shared `RaiseError(...)` helpers come from the `StoredProcedureCommon` base

**User procedures do not see the caller's identity directly.** `IDatabaseContext` no longer exposes `CurrentUser` or `Token`. If a procedure needs the acting user for audit columns, accept it as a public parameter (e.g. `public string CurrentUser { get; set; }`) and have the caller pass it. This keeps procedures transport-agnostic.

### System Procedures (internal)

SmartData's own built-in procedures (all `sp_*`) extend `SystemStoredProcedure<TResult>` or `SystemAsyncStoredProcedure<TResult>` instead of the user-facing bases. Each system procedure's `Execute` signature is:

```csharp
TResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct);
```

The base resolves `RequestIdentity` and `IDatabaseProvider` from the scope and passes them in. The executor seeds the context with a neutral `"master"` default — each procedure must call `db.UseDatabase("master")` or `db.UseDatabase(Database)` (from its own parameter) as its first step. This keeps the target database explicit and greppable.

**Permission checks are imperative.** There is no `[RequirePermission]` attribute. Each procedure calls the appropriate helper on `RequestIdentity` at the top of `Execute`:

```csharp
identity.Require("Users:List");                           // unscoped
identity.RequireScoped("Table:Create", Database);         // db-scoped
identity.RequireAny("Users:Edit:Self", "Users:Edit:Any"); // OR
if (identity.Has("Admin:Settings")) { ... }               // predicate, no throw
```

Semantics:
- **Trusted calls** (scheduler-driven, `IProcedureService`) pass all `Require*` checks silently.
- **Unauthenticated callers** get `UnauthorizedAccessException("Authentication required.")` — identical to the old declarative gate.
- **Admin sessions** pass every check.
- **Regular sessions** are matched against `UserSession.Permissions` using exact, action-wildcard (`Data:Select` matches `Data:*`) and db-wildcard (`acme:Table:List` matches `*:Table:List` and `*:Table:*`) rules.

`RequestIdentity` is an internal scoped service populated once per call by `ProcedureExecutor`:

| Member | Use |
|--------|-----|
| `Session` (`UserSession?`) | Authenticated session, if any |
| `Token` (`string?`) | Raw session token |
| `TrustedUser` (`string?`) | Set when the call is framework-trusted (scheduler owner, `"system"` for `IProcedureService`) |
| `Trusted` (`bool`) | `true` iff `TrustedUser != null` — used to bypass gates |
| `UserId` (`string`) | Best-effort identity for audit columns — authenticated user, trusted caller, or `"anonymous"` |
| `Require*` / `Has*` | Permission helpers described above |

System procedures that operate on a specific database declare a `public string Database { get; set; }` parameter and use `db.UseDatabase(Database)` + `identity.RequireScoped(..., Database)`. Master-only procedures (user management, scheduling, settings, telemetry) hardcode `db.UseDatabase("master")`.

Both bases are `internal` — user code can't extend them. User-level identity concerns are out of scope for the framework; wire whatever you need.

### IDatabaseContext

Registered as a **scoped DI service** — each procedure execution gets its own context instance.

```csharp
public interface IDatabaseContext
{
    // Table access (shared connection — enables joins)
    ITable<T> GetTable<T>() where T : class, new();
    T Insert<T>(T entity) where T : class, new();
    int Update<T>(T entity) where T : class, new();
    int Delete<T>(T entity) where T : class, new();
    int Delete<T>(Expression<Func<T, bool>> predicate) where T : class, new();

    // Full-text search
    List<T> FullTextSearch<T>(string searchTerm, int limit = 100) where T : class, new();

    // Procedure calls
    Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default);
    void QueueExecuteAsync(string spName, object? args = null);

    // Context
    string DatabaseName { get; }
    void UseDatabase(string dbName);

    // DI access — used by SystemStoredProcedure to pull RequestIdentity / IDatabaseProvider
    IServiceProvider Services { get; }
}
```

### GetTable\<T\>() — Direct Table Access

`GetTable<T>()` returns a linq2db `ITable<T>` backed by a shared `DataConnection` for the current database. Unlike `GetRepository<T>()` which opens a new connection per operation, all `GetTable<T>()` calls within a procedure share the same connection — enabling joins, subqueries, and multi-table LINQ queries.

```csharp
public override Task<CustomerDetailResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
{
    var customers = ctx.GetTable<Customer>();
    var contacts = ctx.GetTable<CustomerContact>();

    var query = from c in customers
                where c.Id == Id
                join ct in contacts on c.Id equals ct.CustomerId into cts
                from contact in cts.DefaultIfEmpty()
                select new { c.CompanyName, ContactName = contact.Name };

    var result = query.ToList();
    // ...
}
```

Connection lifecycle is managed by `DatabaseContext` — one connection per database, lazily opened on first `GetTable<T>()` call, automatically disposed when the procedure execution completes.

Schema migration runs automatically on first `GetTable<T>()` or `Insert<T>()` for each entity type.

### Insert / Update / Delete — Entity Write Operations

Write operations use the same shared `DataConnection` as `GetTable<T>()`.

```csharp
// Insert with auto-identity
var customer = ctx.Insert(new Customer { CompanyName = "Acme", CreatedOn = DateTime.UtcNow });
// customer.Id is populated

// Update by primary key
customer.Status = "Active";
ctx.Update(customer);

// Delete by entity
ctx.Delete(customer);

// Delete by predicate
ctx.Delete<CustomerContact>(x => x.CustomerId == customerId);
```

| Method | Description |
|--------|-------------|
| `ctx.Insert<T>(entity)` | Insert entity, returns it with identity populated |
| `ctx.Update<T>(entity)` | Update by PK, returns affected row count |
| `ctx.Delete<T>(entity)` | Delete by PK, returns affected row count |
| `ctx.Delete<T>(predicate)` | Delete matching rows, returns affected row count |

### UseDatabase() — Multi-Database Access

`UseDatabase(dbName)` switches the active database for subsequent `GetTable<T>()` calls. Connections are maintained per-database and all disposed when the procedure completes.

```csharp
public override Task<SomeResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
{
    ctx.UseDatabase("master");
    var customers = ctx.GetTable<Customer>();     // from master database

    ctx.UseDatabase("analytics");
    var events = ctx.GetTable<AnalyticsEvent>();  // from analytics database

    // ...
}
```

`UseDatabase()` only switches a pointer — it does not close existing connections. Previously obtained `ITable<T>` references remain valid. All connections are disposed together when the procedure completes.

### Connection Lifecycle

`DatabaseContext` implements `IDisposable`. The executor wraps each invocation in `using var ctx = ...`, so all `DataConnection` instances opened via `GetTable<T>()` are automatically closed after the procedure returns. Background procedures each get their own independent `DatabaseContext`.

## AutoRepo ORM

Automatic schema migration on first use. Procedures access data via `GetTable<T>()` (returns linq2db `ITable<T>`) and `Insert<T>()` / `Update<T>()` / `Delete<T>()` on `IDatabaseContext`.

### Schema Migration

Automatic on first repository use per connection:

1. **Table missing** -> `CREATE TABLE` from entity definition
2. **Column missing** -> `ALTER TABLE ADD COLUMN`
3. **Type/nullability mismatch** -> Table recreation (create temp, copy data, drop original, rename)
4. **Index missing** -> `CREATE INDEX` from `[Index]` attributes
5. **Index changed** (columns/uniqueness) -> Drop + recreate
6. **Stale index** (prefixed index removed from attributes) -> Auto-drop (when `Index.AutoDrop` is enabled)
7. **Full-text index missing** -> Create FTS index from `[FullTextIndex]` attribute (provider-specific)

Entity mapping reads `[Table]`, `[Column]`, `[PrimaryKey]`, `[Identity]`, `[NotColumn]`, `[Nullable]` attributes. Enums map to Int32. Provider-specific optimizations (e.g. SQLite WAL) are handled by `IDatabaseProvider.OnConnectionCreated()`.

### Index Attributes

Declare indexes and full-text search indexes directly on entity classes:

```csharp
[Table]
[Index("IX_Customer_ContactEmail", nameof(ContactEmail), Unique = true)]
[Index("IX_Customer_Status", nameof(Status))]
[FullTextIndex(nameof(CompanyName), nameof(ContactName), nameof(Notes))]
public class Customer { ... }
```

**`[Index(name, columns...)]`** — Declares a regular index. `AllowMultiple = true`. Properties:
- `Name` — Index name as declared by the developer (e.g. `IX_Customer_Email`). SmartData auto-prefixes with `Index.Prefix` (default `SD_`) when creating the actual database index (e.g. `SD_IX_Customer_Email`). Only indexes with this prefix are eligible for auto-drop.
- `Columns` — One or more column names (must match `[Column]`-annotated properties)
- `Unique` — Whether the index enforces uniqueness (default: `false`)

**`[FullTextIndex(columns...)]`** — Declares a full-text search index. `AllowMultiple = false` (one per entity). All columns must be string-typed `[Column]` properties. Auto-generates name `FTX_{TableName}`.

**`IndexMapping<T>`** — Static generic class that reads attributes, validates columns, and produces `IndexDefinition` records. Mirrors `EntityMapping<T>` pattern with caching and thread safety.

**`IndexDefinition`** — `record(Name, Columns, Unique, IsFullText)` in `ProviderModels.cs`.

### Full-Text Search

Providers implement FTS via `ISchemaOperations` (DDL) and `IDatabaseProvider` (query building):

| Method | Interface | Description |
|--------|-----------|-------------|
| `CreateFullTextIndex(dbName, table, columns)` | `ISchemaOperations` | Creates provider-specific FTS index |
| `DropFullTextIndex(dbName, table)` | `ISchemaOperations` | Drops FTS index |
| `FullTextIndexExists(dbName, table)` | `ISchemaOperations` | Checks FTS index existence |
| `BuildFullTextSearchSql(table, columns, limit)` | `IDatabaseProvider` | Returns provider-specific FTS query SQL |

Query via `IDatabaseContext.FullTextSearch<T>(searchTerm, limit)`:

```csharp
public override Task<SearchResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
{
    var results = ctx.FullTextSearch<Customer>("acme corp", limit: 50);
    return Task.FromResult(new SearchResult { Items = results });
}
```

Returns `List<T>` ordered by relevance. Throws if entity has no `[FullTextIndex]` attribute.

### IndexOptions

Configure index behavior via `SmartDataOptions.Index`:

```csharp
builder.Services.AddSmartData(o =>
{
    o.Index.Prefix = "SD_";              // default — prefix for auto-managed indexes
    o.Index.AutoCreate = true;           // default — auto-create [Index] indexes
    o.Index.AutoDrop = true;             // default — auto-drop stale prefixed indexes
    o.Index.AutoCreateFullText = true;   // default — auto-create [FullTextIndex] indexes
});
```

| Property | Default | Description |
|----------|---------|-------------|
| `Prefix` | `"SD_"` | Prefix added to all attribute-declared index names. Only indexes with this prefix are auto-dropped. |
| `AutoCreate` | `true` | Auto-create indexes from `[Index]` attributes during migration |
| `AutoDrop` | `true` | Auto-drop prefixed indexes that are no longer declared in attributes |
| `AutoCreateFullText` | `true` | Auto-create FTS indexes from `[FullTextIndex]` attributes |

## Database Management

Databases are managed by `DatabaseManager` which delegates to `IDatabaseProvider`. With the SQLite provider, databases are stored as files in a configured data directory (`data/{name}.db`).

- **Master database** — reserved, auto-created with `_sys_users` (default admin:admin), `_sys_user_permissions`, `_sys_settings`, and `_sys_logs`
- **User databases** — created/dropped via procedures; each gets a `_sys_logs` table
- Multi-database support — procedures target a specific database via the RPC request

## Authentication

- PBKDF2-SHA256 hashed passwords stored in `_sys_users` (master database)
- Login returns a random 32-byte Base64 token
- Sessions tracked in-memory (`ConcurrentDictionary`) with expiration
- **Session expiration** — configurable TTL (default 24h) with sliding or absolute expiration. Expired sessions are rejected on access and purged by `SessionCleanupService` (runs every 60s by default)
- **Session revocation** — disabling or deleting a user immediately revokes all their active sessions via `SessionManager.RevokeUserSessions()`
- All user procedures require authentication — there is no user-accessible opt-out attribute. `[AllowAnonymous]` is framework-internal. For trusted startup/background work, call procedures through `IProcedureService` (which bypasses the auth gate) instead.

### SessionOptions

Configure session behavior via `SmartDataOptions.Session`:

```csharp
builder.Services.AddSmartData(o =>
{
    o.Session.SessionTtl = TimeSpan.FromHours(24);    // default — session lifetime
    o.Session.SlidingExpiration = true;                // default — reset TTL on activity
    o.Session.CleanupIntervalSeconds = 60;             // default — purge interval
});
```

| Property | Default | Description |
|----------|---------|-------------|
| `SessionTtl` | `24 hours` | Session time-to-live. Sliding: inactivity timeout. Absolute: max lifetime. |
| `SlidingExpiration` | `true` | When true, TTL resets on each request. When false, sessions expire at creation + TTL. |
| `CleanupIntervalSeconds` | `60` | How often the background service scans for expired sessions. |

## Health Checks

`UseSmartData()` maps a `GET /health` endpoint (no authentication required) using ASP.NET Core health checks. Returns JSON:

```json
{
  "status": "Healthy",
  "duration": "00:00:00.0012345",
  "checks": [{
    "name": "smartdata",
    "status": "Healthy",
    "description": "OK",
    "data": { "uptime": "1.02:30:15", "active_sessions": 3, "db_reachable": true }
  }]
}
```

Checks: database connectivity (opens connection to master DB), reports uptime and active session count.

## Permissions (RBAC)

Two-tier permission model defined in `Permissions.cs`. Each permission is a `public const string` field with a `[Description]` attribute. Field names follow PascalCase convention (e.g. `DatabaseDrop` → `"Database:Drop"`, `TableAll` → `"Table:*"`). Keys use `:` delimiter. Startup validation ensures each field's value matches its name.

### System Permissions (`Permissions.System`)

Global permissions not scoped to any database.

| Property | Key | Description |
|----------|-----|-------------|
| `DatabaseAll/Create/Drop/List` | `Database:*`, `Database:Create`, etc. | Database lifecycle |
| `BackupAll/Create/Drop/List/Restore/Download/Upload/History` | `Backup:*`, `Backup:Create`, etc. | Backup & restore |
| `UserAll/Create/Grant/Revoke/Delete/List` | `User:*`, `User:Create`, etc. | User management |
| `ServerAll/Storage/Logs/Errors/Metrics` | `Server:*`, `Server:Storage`, etc. | Monitoring & diagnostics |
| `SchedulerAll/List/Edit/Run/Cancel` | `Scheduler:*`, `Scheduler:List`, etc. | Scheduling subsystem |

### Scoped Permissions (`Permissions.Scoped`)

Per-database permission templates. At runtime, prefix with database name or `*` wildcard.
Examples: `mydb:Table:Create`, `*:Data:Select`, `analytics:Index:*`

| Property | Key | Description |
|----------|-----|-------------|
| `TableAll/Create/Drop/List/Describe/Rename` | `Table:*`, `Table:Create`, etc. | Table management |
| `ColumnAll/Add/Drop/List/Rename` | `Column:*`, `Column:Add`, etc. | Column management |
| `IndexAll/Create/Drop/List` | `Index:*`, `Index:Create`, etc. | Index management |
| `DataAll/Select/Insert/Update/Delete/Export/Import/Dump` | `Data:*`, `Data:Select`, etc. | Data operations |

### Permission Enforcement

All procedures (system and user-defined) can be annotated with `[RequirePermission]` attributes, enforced centrally by `ProcedureExecutor` before execution:

```csharp
[RequirePermission(Permissions.DatabaseCreate)]              // system permission
[RequirePermission(Permissions.TableCreate, Scoped = true)]  // scoped — executor prefixes dbName
```

Exceptions: `SpLogin` (anonymous) and `SpLogout` (any authenticated user) have no permission requirement.

### Permission Model Classes

- **Permission** — Immutable value type: `Key`, `Description`, `Segments` (colon-split), `Action` (last segment)
- **Permissions** — `const string` fields with `[Description]` attributes (usable in `[RequirePermission]` attributes). `System` and `Scoped` arrays of `Permission` objects built via reflection at startup with key validation.
- **RequirePermissionAttribute** — Declarative permission check on procedure classes (`Key`, `Scoped`)

## Background Execution

Background work is queued internally via `BackgroundSpQueue`. Uses an unbounded async channel processed by `BackgroundSpService` (hosted service). Failures are logged but don't crash the service. Note: `QueueExec()` is an internal method on `DatabaseContext`, not exposed on the `IDatabaseContext` interface.

`SessionCleanupService` is another hosted service that periodically scans for and removes expired sessions based on `SessionOptions.CleanupIntervalSeconds`.

## QueryFilterBuilder

Converts JSON filter objects to parameterized SQL WHERE clauses. Prevents SQL injection.

```csharp
// Input:  { "age": { "$gte": 18 }, "status": { "$in": ["active", "pending"] } }
// Output: [age] >= @p0 AND [status] IN (@p0_0, @p0_1)
```

**Operators:** `$gt`, `$gte`, `$lt`, `$lte`, `$ne`, `$like`, `$starts`, `$ends`, `$contains`, `$in`, `$nin`, `$null`, `$notnull`, `$and`, `$or`. Direct value means equality.

## System Procedures (63 built-in)

### Authentication
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_login` | Username, Password | Returns session token (anonymous allowed) |
| `sp_logout` | Token | Ends session |

### Database Management
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_database_create` | Name | Creates new SQLite database |
| `sp_database_drop` | Name | Deletes database (not master) |
| `sp_database_list` | — | Lists all databases (excludes master) |

### Table Management
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_table_create` | Name, Columns (JSON) | Creates table with column definitions |
| `sp_table_drop` | Name | Drops table |
| `sp_table_rename` | Name, NewName | Renames table |
| `sp_table_list` | — | Lists tables (excludes _sys_*, sqlite_*) |
| `sp_table_describe` | Name | Returns columns and indexes |
| `sp_dump` | — | Markdown documentation of all tables |

### Column Management
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_column_add` | Table, Name, Type, Nullable | Adds column |
| `sp_column_drop` | Table, Name | Drops column |
| `sp_column_rename` | Table, Name, NewName | Renames column |
| `sp_column_list` | Table | Lists columns with metadata |

### Data Operations
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_select` | Table, Where, OrderBy, Limit, Offset | Query with JSON filters |
| `sp_insert` | Table, Values (JSON) | Insert row, returns last ID |
| `sp_update` | Table, Where, Set (JSON) | Update matching rows |
| `sp_delete` | Table, Where (JSON, required) | Delete matching rows |

### Index Management
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_index_create` | Table, Name, Columns, Unique | Creates index |
| `sp_index_list` | Table | Lists indexes |
| `sp_index_drop` | Name | Drops index |

### Backup & Restore
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_backup_create` | Databases ("db1,db2" or "*") | Submits async create job, returns JobId + BackupId immediately |
| `sp_backup_restore` | BackupId, Force | Submits async restore job, returns JobId immediately |
| `sp_backup_status` | JobId | Polls job progress (Status, Progress 0-1, ProgressMessage, Size, ElapsedMs, Error) |
| `sp_backup_cancel` | JobId | Cancels a running backup/restore job |
| `sp_backup_list` | — | Lists available backups (reads sidecar manifests, not zip files) |
| `sp_backup_download` | BackupId, Offset, ChunkSize | Chunked backup download (1MB default) |
| `sp_backup_upload` | BackupId, Data, Offset, TotalSize | Chunked backup upload |
| `sp_backup_drop` | BackupId | Deletes backup + sidecar manifest |
| `sp_backup_history` | — | Returns backup operation history (one JSON file per event) |

All backup procedures delegate to `BackupService` (registered as singleton). Create and restore operations run as async background jobs via `BackupJobRunner` (sequential — one job at a time). Callers receive a `JobId` immediately and poll `sp_backup_status` for progress.

**Storage layout** (`{DataDirectory}/_backups/`):
- `backups/` — `.smartbackup` archives + `.json` sidecar manifests (for fast listing without opening zips)
- `history/` — one JSON file per operation, named `{timestamp}_{backupId}_{operation}.json`
- `jobs/` — ephemeral job files for crash diagnostics

**Retention** (configured via `SmartDataOptions.Backup`): `MaxBackupAge` (days), `MaxBackupCount`, `MaxHistoryAge` (days), `MaxHistoryCount`. Cleanup runs on startup and after each create/upload/restore. On first startup, migrates flat `_backups/` layout and `history.json` to new structure.

The `.smartbackup` format is a zip containing:
- `backup.json` — manifest with version, id, createdAt, databases list, SHA256 checksums
- `databases/{db}/_schema.json` — table definitions with logical types, columns, indexes
- `databases/{db}/{table}.bin` — binary-serialized rows (BinarySerializer IDataReader format with key interning)

### Data Import/Export
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_data_export` | Table, Where | Export rows as JSON |
| `sp_data_import` | Table, Rows (JSON), Mode?, Truncate?, DryRun? | Import rows (transactional). Mode: "insert" (default, fail on dup), "skip" (INSERT OR IGNORE), "replace" (INSERT OR REPLACE). Truncate: delete all before import. DryRun: validate only. |

### Logging & Monitoring
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_logs` | Limit (default 50) | Recent log entries |
| `sp_errors` | Name, Limit | Error and compilation logs |
| `sp_storage` | Database | Database and backup sizes |

### Metrics & Observability
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_metrics` | Name?, Type?, Source? (live/db), From?, To?, Page, PageSize | Query counters, histograms, gauges (live + historical) |
| `sp_traces` | TraceId?, Procedure?, Source?, ErrorsOnly?, MinDurationMs?, From?, To?, Page, PageSize | Query spans grouped into traces |
| `sp_exceptions` | ExceptionType?, Procedure?, Source?, From?, To?, Page, PageSize | Query captured exceptions with context |

**Built-in metric names:**
- `rpc.requests` (counter) — total procedure calls, tagged by procedure/db/error
- `rpc.errors` (counter) — error count by procedure/db/error_type
- `rpc.duration_ms` (histogram) — procedure latency with p50/p95/p99
- `rpc.active_requests` (gauge) — concurrent in-flight requests
- `sql.queries` (counter) — SQL command count by table/operation
- `sql.duration_ms` (histogram) — SQL latency with percentiles
- `sql.rows` (counter) — rows affected
- `auth.login_attempts` (counter) — login success/failure count
- `auth.logouts` (counter) — logout count
- `auth.active_sessions` (gauge) — current session count

### User Management
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_user_create` | Username, Password | Creates user in master database |
| `sp_user_get` | UserId | Gets user details with permissions |
| `sp_user_update` | UserId, Username?, Password?, IsAdmin?, IsDisabled? | Updates user fields; disabling revokes active sessions |
| `sp_user_delete` | UserId | Deletes non-admin user, revokes active sessions |
| `sp_user_permission_grant` | Username, PermissionKey | Grants a permission to a user |
| `sp_user_permission_revoke` | Username, PermissionKey | Revokes a permission from a user |
| `sp_user_permission_list` | Username | Lists all permissions for a user |

### Settings
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_settings_list` | — | Lists all settings (key, value, section, read-only status, modified date) |
| `sp_settings_update` | Key, Value | Updates a runtime-tunable setting (persists to `_sys_settings` table + in-memory) |

Settings are stored in the `_sys_settings` table in the master database. Startup-only settings (SchemaMode, Index.*) cannot be updated at runtime.

### Scheduling
| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_schedule_list` | Search?, Enabled? | Paginated list of schedules with last-run outcome |
| `sp_schedule_get` | Id, RecentRuns? | One schedule + recent `SysScheduleRun` rows + Job metadata |
| `sp_schedule_update` | Id, Enabled?, RetryAttempts?, RetryIntervalSeconds?, JitterSeconds? | Updates user-controllable fields only. Timing is owned by code attributes |
| `sp_schedule_delete` | Id | Removes schedule + its `SysScheduleRun` rows. Attribute-sourced schedules recreate on next startup |
| `sp_schedule_preview` | Id, Count? | Returns next N fire times via `SlotComputer` |
| `sp_schedule_history` | ScheduleId?, ProcedureName?, Outcome?, Since?, Until?, Limit? | `SysScheduleRun` rows with optional filters |
| `sp_schedule_stats` | WindowHours? | Counts + per-procedure avg duration over the window |
| `sp_schedule_start` | Id | Manually triggers a run — inserts `SysScheduleRun` claimed, queues `sp_schedule_execute` |
| `sp_schedule_cancel` | ScheduleId? or RunId? | Flips `CancelRequested = true` on in-flight runs |
| `sp_schedule_execute` | RunId | **Internal.** Executes one pre-claimed run; heartbeat + cancel watcher; retry-on-fail |
| `sp_scheduler_tick` | — | **Internal.** Four-step pump: due → claim + queue, bounded catch-up, pending retries, orphan sweep |
| `sp_schedule_run_retention` | — | Built-in `[Daily("03:15")]` job that trims `_sys_schedule_runs` older than `HistoryRetentionDays` |

See **[Scheduling](#scheduling-1)** section below for the full design.

## Scheduling

First-class scheduled execution for any registered procedure. Developers decorate procedures with `[Daily]`/`[Every]`/`[Weekly]`/`[Monthly]`/`[MonthlyDow]`/`[Once]`; the reconciler materializes those into the `_sys_schedules` table on startup; a `JobScheduler` hosted service polls `sp_scheduler_tick` on a timer. Code is the source of truth for *what fires and when*; the DB stores runtime state and a few user-controlled ops knobs (enable toggle, retry, jitter).

### Guiding principle

> A scheduled job is a stored procedure with a `[Daily]` on it. Developers change what fires and when. Users toggle it on/off and tune retry/jitter.

### Entities

- **`SysSchedule`** (`_sys_schedules`) — one row per timing rule. Unique on `(ProcedureName, Name)`. Flat timing fields (`FreqType`, `FreqInterval`, `FreqUnit`, bitmask columns for days/months/weeks, `TimeOfDay`, `BetweenStart/End`, `RunOnce`, `JitterSeconds`, `StartDate/EndDate`, `NextRunOn`, `LastRunOn`). Retry policy (`RetryAttempts`, `RetryIntervalSeconds`). Timing fields are overwritten from code attributes on every startup; user-controlled fields (`Enabled`, `RetryAttempts`, `RetryIntervalSeconds`, `JitterSeconds`) are preserved across reconciles.
- **`SysScheduleRun`** (`_sys_schedule_runs`) — one row per fire attempt. **Unique on `(ScheduleId, ScheduledFireTime, AttemptNumber)` — the entire multi-instance concurrency story.** Also tracks `Outcome` (`Claimed | Running | Succeeded | Failed | Cancelled`), `StartedOn`, `FinishedOn`, `DurationMs`, `AttemptNumber`, `LastHeartbeatAt` (liveness), `CancelRequested` (cooperative), `NextAttemptAt` (retry queue), `InstanceId`.

### Schedule attributes

All times are **server local time**. Validation happens on the reconciliation pass, not at compile time — a bad `"HH:mm"` throws at app boot.

| Attribute | Example | Materializes |
|-----------|---------|--------------|
| `[Daily]` | `[Daily("02:00")]`, `[Daily(2, 0)]` | One fire per day at `TimeOfDay`; `Days` filter narrows to weekdays |
| `[Every]` | `[Every(5, Unit.Minutes)]`, `[Every(1, Unit.Hours, Between = "09:00-17:00", Days = Days.Weekdays)]` | Wall-clock anchors. `Between` bounds daily window |
| `[Weekly]` | `[Weekly(Days.Mon \| Days.Fri, "06:00")]` | Weekly at TimeOfDay on selected weekdays. `Every = N` gives biweekly, etc. |
| `[Monthly]` | `[Monthly(Day.D1 \| Day.Last, "00:30")]` | Specific calendar days (`D29/30/31` skip months that don't have them) |
| `[MonthlyDow]` | `[MonthlyDow(Weeks.First, Days.Mon, "06:00")]` | Nth weekday of month (first Monday, last Friday, etc.) |
| `[Once]` | `[Once("2026-06-01 09:00")]` | Single fire; schedule auto-disables after |
| `[Job]` | `[Job("Nightly DB Maintenance", Category = "Ops")]` | **Code-only** metadata (never persisted) — name, category, description, owner |
| `[Retry]` | `[Retry(attempts: 3, intervalSeconds: 60)]` | Seeds `RetryAttempts` (**TOTAL** runs: 3 = 1 initial + 2 retries) and `RetryIntervalSeconds`. `ErrorSeverity.Fatal` errors always skip retry |

Calendar filters (`Days`, `Months`, `Weeks`, `Day`, `Between`) are `[Flags]` enums/properties that compose onto any cadence. Stacking multiple schedule attributes on the same class produces multiple rows; names are payload-derived (`Daily_09_00`, `Every_5m`, `Weekly_Mon-Wed-Fri_06_00`) so reordering doesn't detach user customizations.

### Registration

```csharp
builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
builder.Services.AddSmartDataScheduler(o =>
{
    o.Enabled              = true;
    o.PollInterval         = TimeSpan.FromSeconds(15);
    o.MaxConcurrentRuns    = 4;
    o.HistoryRetentionDays = 30;
    o.HeartbeatInterval    = TimeSpan.FromSeconds(3);
    o.OrphanTimeout        = TimeSpan.FromMinutes(5);
    o.MaxCatchUp           = 0;           // 0 = drop missed fires; >0 = queue up to N
});
```

`AddSmartDataScheduler` must be called **after** `AddStoredProcedures` so every registered assembly is visible to the reconciler. It registers:

1. `SchedulerOptions` bound from `SmartDataOptions.Scheduler`
2. `ScheduleReconciler` singleton
3. `ScheduleReconciliationHostedService` — runs reconciler synchronously in `StartAsync` (awaited before other hosted services tick)
4. `JobScheduler` — the pump calling `sp_scheduler_tick` at `PollInterval`

### Reconciliation rules (per startup)

| Situation | Action |
|-----------|--------|
| Attribute exists, no matching DB row | **Insert** using attribute values (timing + retry + jitter seed). |
| Attribute exists, matching DB row | **Overwrite timing fields** from attribute. **Preserve** `Enabled`, `RetryAttempts`, `RetryIntervalSeconds`, `JitterSeconds` — these are user-tuned ops knobs. |
| No matching attribute, DB row exists | **Disable** (not delete) — keeps row so `SysScheduleRun` history stays viewable. |
| Schedule whose smallest interval is below `PollInterval` | **Disable** with warning. |

### Execution path

1. **`JobScheduler`** (BackgroundService) calls `sp_scheduler_tick` every `PollInterval`.
2. **`sp_scheduler_tick`** does four steps atomically per tick:
   - Due schedules → insert `SysScheduleRun` with `Outcome = "Claimed"`; on success advance `NextRunOn` via `SlotComputer` and queue `sp_schedule_execute`. Unique index rejects duplicate claims across instances.
   - Bounded catch-up: queue up to `MaxCatchUp` missed fires; drop the rest.
   - Pending retries: `Outcome = "Failed" AND NextAttemptAt <= Now` → insert new row with `AttemptNumber + 1`, null source's `NextAttemptAt`, queue.
   - Orphan sweep: `Claimed`/`Running` rows with stale `LastHeartbeatAt` (older than `OrphanTimeout`) → mark `Failed`.
3. **`sp_schedule_execute`** runs one pre-claimed run to completion:
   - Flip `Outcome` to `Running`, bump `LastHeartbeatAt`.
   - Spawn a heartbeat + cancel-watcher task (`HeartbeatInterval`, ~3s) in its own scope — polls `CancelRequested` and triggers `OperationCanceledException` if set.
   - Resolve target procedure via `ProcedureCatalog`, instantiate via `ActivatorUtilities`, invoke directly (bypasses the permission gate — scheduler jobs run under framework authority, not user authority).
   - On `ProcedureException`, set `NextAttemptAt` if `AttemptNumber < RetryAttempts` and `Severity != Fatal`.
   - Persist final `Outcome` + `DurationMs`.

### Production notes

- **All times are server local time.** No per-schedule timezone override. DST hazards (spring-forward gap, fall-back duplicate) are documented in the proposal.
- **Retry counting** — `[Retry(attempts: 3)]` means **3 total runs**, not 3 retries after the first. Differs from common retry libraries; use `attempts: 1` for no retry.
- **Multi-instance safety** is entirely the unique index on `SysScheduleRun(ScheduleId, ScheduledFireTime, AttemptNumber)`. Liveness tracked via `LastHeartbeatAt`, not `StartedOn`, so long-running jobs on another node are never mistaken for crashes.
- **History retention** — the built-in `sp_schedule_run_retention` schedule runs itself; if the scheduler is disabled on a node pointed at shared storage, operators must either enable it elsewhere or trigger it manually.
- **No ambient transactions** — a scheduled procedure calling three sub-procedures can fail partway. Order operations so the hardest-to-undo step runs last.

## ID Generation

`IdGenerator.NewId()` (in SmartData.Core) produces 32-character Base62 IDs combining 8 bytes of `DateTime.UtcNow.Ticks` + 16 bytes of `Guid`, ensuring time-sortable uniqueness.
