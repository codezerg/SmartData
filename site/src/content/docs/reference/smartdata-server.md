---
title: SmartData.Server
description: Engine package — AutoRepo, procedures, scheduler, backups, auth.
---

Core engine. AutoRepo ORM, stored procedure framework, binary RPC, scheduler,
session/auth, backups. Database provider is separate — see
[SmartData.Server.Sqlite](/reference/smartdata-server-sqlite/) or
[SmartData.Server.SqlServer](/reference/smartdata-server-sqlserver/).

For mental models: [Fundamentals → Procedures](/fundamentals/procedures/),
[Entities](/fundamentals/entities/), [Database context](/fundamentals/database-context/),
[Scheduling](/fundamentals/scheduling/).

## Namespaces at a glance

| Folder | Contents |
|--------|----------|
| `Api/` | `CommandRouter` — `/rpc` request dispatch |
| `AutoRepo/` | Entity mapping, schema inspection/migration |
| `AutoRepo/Migration/` | `SchemaInspector`, `SchemaManager`, `SchemaMigrator` |
| `Attributes/` | `[Index]`, `[FullTextIndex]` |
| `Backup/` | `BackupService`, job runner, manifest + retention models |
| `Engine/` | `DatabaseManager`, `DatabaseContext`, `SessionManager`, `ProcedureExecutor`, `ProcedureCatalog`, `KeyValueStore`, `SettingsService`, `QueryFilterBuilder`, background queue |
| `Entities/` | `SysUser`, `SysUserPermission`, `SysSetting`, `SysLog`, `SysMetric`, `SysSpan`, `SysException`, `SysSchedule`, `SysScheduleRun` |
| `Metrics/` | `MetricsCollector`, `Counter`, `Histogram`, `Gauge`, `Span`, ring buffers, flush service |
| `Procedures/` | `IStoredProcedure`, `StoredProcedure<T>`, `AsyncStoredProcedure<T>`, `IDatabaseContext`, `IKeyValueStore` |
| `Providers/` | `IDatabaseProvider`, `ISchemaProvider`, `ISchemaOperations`, `IRawDataProvider`, `SchemaMode` |
| `Scheduling/` | Attributes, `SlotComputer`, `ScheduleReconciler`, `JobScheduler`, `SchedulerOptions` |
| `SystemProcedures/` | 63 built-in `sp_*` procedures ([catalog](/reference/system-procedures/)) |

## Registration

```csharp
builder.Services.AddSmartData(o => { /* SmartDataOptions */ });
builder.Services.AddSmartDataSqlite();                              // or AddSmartDataSqlServer
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
builder.Services.AddSmartDataScheduler(o => { /* SchedulerOptions */ });  // optional; must come AFTER AddStoredProcedures

var app = builder.Build();
app.UseSmartData();   // maps POST /rpc + GET /health
```

`AddSmartData()` registers: `DatabaseManager`, `SessionManager`, `ProcedureCatalog`, `ProcedureExecutor`, `CommandRouter`, `BackgroundSpQueue` + service, `BackupService` + job runner, `SessionCleanupService`, `SmartDataHealthCheck`.

| Registration | Purpose |
|--------------|---------|
| `AddSmartData(Action<SmartDataOptions>?)` | Core singletons + health check. |
| `AddStoredProcedures(Assembly)` | Scans for `IStoredProcedure` / `IAsyncStoredProcedure`. PascalCase → snake_case; `sp_` for Server assembly, `usp_` elsewhere. Leading `Sp` stripped. |
| `AddSmartDataScheduler(Action<SchedulerOptions>?)` | Reconciler + `JobScheduler` hosted service. |
| `UseSmartData()` | Maps `/rpc` (binary `CommandRequest`/`CommandResponse`) and `/health`. |

## SmartDataOptions

| Property | Type | Default | Notes |
|----------|------|---------|-------|
| `SchemaMode` | `SchemaMode` | `Auto` | `Auto`: migrate on first entity use. `Manual`: no automatic DDL. |
| `IncludeExceptionDetails` | `bool` | `true` in Development, else `false` | Whether RPC errors expose `ex.Message` to callers. `ProcedureException` messages always flow through. |
| `Session` | `SessionOptions` | — | Session TTL + cleanup. |
| `Index` | `IndexOptions` | — | Auto-create/drop rules. |
| `Backup` | `BackupOptions` | — | Retention. |
| `Scheduler` | `SchedulerOptions` | — | Bound by `AddSmartDataScheduler`. |
| `Metrics` | `MetricsOptions` | — | Instrumentation. |

### SessionOptions

| Property | Default | Notes |
|----------|---------|-------|
| `SessionTtl` | `24h` | Sliding = inactivity timeout. Absolute = max lifetime. |
| `SlidingExpiration` | `true` | Reset TTL on each authenticated call. |
| `CleanupIntervalSeconds` | `60` | Expired-session purge cadence. |

### IndexOptions

| Property | Default | Notes |
|----------|---------|-------|
| `Prefix` | `"SD_"` | Applied to `[Index]` names. Only prefixed indexes are eligible for auto-drop. |
| `AutoCreate` | `true` | Create `[Index]` indexes during migration. |
| `AutoDrop` | `true` | Drop prefixed indexes no longer declared. |
| `AutoCreateFullText` | `true` | Create `[FullTextIndex]` indexes. |

### BackupOptions

| Property | Notes |
|----------|-------|
| `MaxBackupAge` | Days; older archives pruned after each operation. |
| `MaxBackupCount` | Keep latest N archives. |
| `MaxHistoryAge` | Days; older `history/` JSON pruned. |
| `MaxHistoryCount` | Keep latest N history entries. |

### SchedulerOptions

| Property | Default | Notes |
|----------|---------|-------|
| `Enabled` | `true` | Disables the pump; reconciler still runs. |
| `PollInterval` | `15s` | `JobScheduler` tick cadence. Schedules with smaller intervals are disabled on startup with a warning. |
| `MaxConcurrentRuns` | `4` | Upper bound on concurrent `sp_schedule_execute` runs. |
| `HistoryRetentionDays` | `30` | `sp_schedule_run_retention` trims `_sys_schedule_runs` older than this. |
| `HeartbeatInterval` | `3s` | Cancel-watcher + liveness write cadence. |
| `OrphanTimeout` | `5m` | Stale `LastHeartbeatAt` threshold before orphan sweep marks `Failed`. |
| `MaxCatchUp` | `0` | Missed fires queued per schedule per tick (0 drops). Only enable for idempotent jobs. |
| `InstanceId` | host:pid | Written to `_sys_schedule_runs.InstanceId`. |

## Procedure callers

Registered as scoped services:

| Service | Authority | Auth gate | Audit user |
|---------|-----------|-----------|------------|
| `IProcedureService` | Framework (trusted) | Bypassed | `"system"` |
| `IAuthenticatedProcedureService` | Per-session token | Anonymous rejected (except framework-internal `[AllowAnonymous]`) | Session's `UserId` |

The `/rpc` pipeline and the embedded console both use `IAuthenticatedProcedureService`. Startup tasks and schedulers use `IProcedureService`.

## IDatabaseContext

Scoped; one per procedure execution. Implements `IDisposable`.

```csharp
public interface IDatabaseContext
{
    ITable<T> GetTable<T>() where T : class, new();
    T   Insert<T>(T entity) where T : class, new();
    int Update<T>(T entity) where T : class, new();
    int Delete<T>(T entity) where T : class, new();
    int Delete<T>(Expression<Func<T, bool>> predicate) where T : class, new();

    List<T> FullTextSearch<T>(string searchTerm, int limit = 100) where T : class, new();

    Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default);
    void    QueueExecuteAsync(string spName, object? args = null);

    string DatabaseName { get; }
    void   UseDatabase(string dbName);

    IServiceProvider Services { get; }
}
```

Concept notes in [Fundamentals → Database context](/fundamentals/database-context/). Transactions: `ctx.BeginTransaction()` (extension via linq2db).

## Procedure base classes

| Base | For | Override |
|------|-----|----------|
| `StoredProcedure<TResult>` | User sync procedures | `TResult Execute(IDatabaseContext, CancellationToken)` |
| `AsyncStoredProcedure<TResult>` | User async procedures | `Task<TResult> ExecuteAsync(IDatabaseContext, CancellationToken)` |
| `SystemStoredProcedure<TResult>` *(internal)* | Built-in `sp_*` | `Execute(RequestIdentity, IDatabaseContext, IDatabaseProvider, CancellationToken)` |
| `SystemAsyncStoredProcedure<TResult>` *(internal)* | Built-in `sp_*` async | same, async |

Shared helpers on `StoredProcedureCommon`:

| Helper | Throws | Notes |
|--------|--------|-------|
| `RaiseError(string)` | `ProcedureException` | Message ID `0`. |
| `RaiseError(int id, string, ErrorSeverity = Error)` | `ProcedureException` | `[DoesNotReturn]`. IDs `0–999` system, `1000+` user. `Fatal` skips scheduler retry. |

User code cannot extend the `System*` bases; they are `internal`.

### RequestIdentity (internal scoped service)

Passed into system procedures. Populated once per call by `ProcedureExecutor`.

| Member | Use |
|--------|-----|
| `Session` (`UserSession?`) | Authenticated session, if any. |
| `Token` (`string?`) | Raw session token. |
| `TrustedUser` (`string?`) | Set when `IProcedureService` or scheduler. |
| `Trusted` (`bool`) | `TrustedUser != null` — bypasses gates. |
| `UserId` (`string`) | Auth'd user, trusted caller, or `"anonymous"`. |
| `Require(key)` / `RequireScoped(key, db)` / `RequireAny(...)` / `Has(key)` | Imperative permission checks. Admin passes all; trusted passes all; regular matched with exact + action-wildcard (`Data:*`) + db-wildcard (`*:Table:List`). |

## Provider interfaces

| Interface | Responsibility |
|-----------|----------------|
| `IDatabaseProvider` | Connections, database lifecycle (create/drop/list), `DataDirectory`, `OnConnectionCreated`, `BuildFullTextSearchSql`. |
| `ISchemaProvider` | Read-only metadata — tables, columns, indexes, row counts. `GetTableSchema()` batches table/columns/indexes in one connection. |
| `ISchemaOperations` | DDL execution, type mapping (forward via `MapType`, reverse via `MapTypeReverse`), defaults, FTS create/drop/exists. |
| `IRawDataProvider` | Dynamic CRUD, import/export, raw SQL, streaming `OpenReader`. |

Backups are provider-agnostic (`BackupService`). How-to: [Write a custom provider](/how-to/write-a-custom-provider/).

## Index attributes

```csharp
[Index("IX_Customer_Email", nameof(Email), Unique = true)]
[Index("IX_Customer_Status", nameof(Status))]
[FullTextIndex(nameof(Name), nameof(Notes))]
public class Customer { ... }
```

| Attribute | Multi | Properties |
|-----------|-------|-----------|
| `[Index(name, columns...)]` | yes | `Name`, `Columns`, `Unique` (default `false`). Auto-prefixed with `IndexOptions.Prefix`. |
| `[FullTextIndex(columns...)]` | no | All columns must be string `[Column]` properties. Auto-name `FTX_{TableName}`. |

`IndexMapping<T>` produces `IndexDefinition(Name, Columns, Unique, IsFullText)` with caching.

## Permissions

Defined in `Permissions.cs` as `public const string` fields with `[Description]`. PascalCase → `:`-delimited keys (`DatabaseDrop` → `"Database:Drop"`). Validated at startup.

### System (unscoped)

`Database:*`, `Backup:*`, `User:*`, `Server:*`, `Scheduler:*` (each with specific actions: `Create`, `Drop`, `List`, `Grant`, `Revoke`, `Storage`, `Logs`, `Errors`, `Metrics`, `Restore`, `Download`, `Upload`, `History`, `Edit`, `Run`, `Cancel`).

### Scoped (`Permissions.Scoped`)

Per-database templates prefixed with db name or `*`: `Table:*`, `Column:*`, `Index:*`, `Data:*` (`Select`, `Insert`, `Update`, `Delete`, `Export`, `Import`, `Dump`).

Examples: `mydb:Table:Create`, `*:Data:Select`, `analytics:Index:*`.

### Types

| Type | Notes |
|------|-------|
| `Permission` | `Key`, `Description`, `Segments`, `Action`. |
| `Permissions` | `System[]`, `Scoped[]` built via reflection at startup. |
| `RequirePermissionAttribute` | Declarative marker on user procedures (`Key`, `Scoped`). System procedures use imperative `identity.Require*` instead. |

## QueryFilterBuilder

JSON filter → parameterized `WHERE` clause. Used by `sp_select`, `sp_update`, `sp_delete`.

Operators: `$gt`, `$gte`, `$lt`, `$lte`, `$ne`, `$like`, `$starts`, `$ends`, `$contains`, `$in`, `$nin`, `$null`, `$notnull`, `$and`, `$or`. Bare value = equality.

## Authentication & sessions

- PBKDF2-SHA256 passwords in `_sys_users` (master DB).
- Login returns a random 32-byte Base64 token.
- Sessions tracked in-memory (`ConcurrentDictionary`) with expiration per `SessionOptions`.
- `SessionManager.RevokeUserSessions(userId)` — called by `sp_user_delete` and disable paths.
- `SessionCleanupService` hosted service purges expired sessions on `CleanupIntervalSeconds`.

## Backup storage layout

Under `{DataDirectory}/_backups/`:

| Path | Contents |
|------|----------|
| `backups/*.smartbackup` | Zip archive. Manifest + schema + binary-serialized rows. |
| `backups/*.json` | Sidecar manifest — enables fast listing without opening zips. |
| `history/{ts}_{id}_{op}.json` | One file per operation event. |
| `jobs/` | Ephemeral; used for crash diagnostics. |

Archive contents:

| Entry | Contents |
|-------|----------|
| `backup.json` | Manifest: version, id, createdAt, databases, SHA256 checksums. |
| `databases/{db}/_schema.json` | Table definitions — logical types, columns, indexes. |
| `databases/{db}/{table}.bin` | `BinarySerializer` `IDataReader` rows with key interning. |

## Health check

`GET /health` (anonymous). Checks master DB connectivity. Data: `uptime`, `active_sessions`, `db_reachable`.

## System procedures

63 built-in `sp_*` procedures grouped by concern (auth, database, table, column, data, index, backup, import/export, logs, metrics, user, settings, scheduling).

Full catalog: [System procedures reference](/reference/system-procedures/).

## Metrics

Built-in instruments via `MetricsCollector`:

| Name | Type | Tags |
|------|------|------|
| `rpc.requests` | counter | procedure, db, error |
| `rpc.errors` | counter | procedure, db, error_type |
| `rpc.duration_ms` | histogram | procedure, db |
| `rpc.active_requests` | gauge | — |
| `sql.queries` | counter | table, operation |
| `sql.duration_ms` | histogram | table, operation |
| `sql.rows` | counter | table, operation |
| `auth.login_attempts` | counter | result |
| `auth.logouts` | counter | — |
| `auth.active_sessions` | gauge | — |

`SqlTrackingInterceptor` instruments all linq2db commands. Spans tracked via `AsyncLocal`. Exceptions captured via `ExceptionRecord`. Flushed to rolling daily DBs by `MetricsFlushService`. Query via `sp_metrics` / `sp_traces` / `sp_exceptions`.

## ID generation

`IdGenerator.NewId()` (in SmartData.Core) → 32-char Base62. 8 bytes `DateTime.UtcNow.Ticks` + 16 bytes `Guid`. Time-sortable.
