---
title: System procedures
description: All built-in sp_* procedures, grouped by concern.
---

Catalog of built-in `sp_*` procedures. All live under `SmartData.Server.SystemProcedures`. Parameters are bound by property name (case-insensitive). `[internal]` procedures are framework-only — scheduler pump and similar; user code cannot call them.

Related: [Permissions](/reference/smartdata-server/#permissions), [RequestIdentity](/reference/smartdata-server/#requestidentity-internal-scoped-service), [QueryFilterBuilder](/reference/smartdata-server/#queryfilterbuilder).

## Authentication & session

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_login` | `Username: string`, `Password: string` | Authenticate; returns session token. Anonymous. |
| `sp_logout` | `Token: string` | Revokes session. |
| `sp_session` | — | Current session info: `UserId`, `Username`, `IsAdmin`, `Permissions`. |

## Databases

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_database_create` | `Name: string` | Creates a new database. |
| `sp_database_drop` | `Name: string` | Drops a database. Master is protected. |
| `sp_database_list` | — | Lists databases with metadata. |

## Tables

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_table_create` | `Database: string`, `Name: string`, `Columns: string` | Creates a table from JSON column definitions. |
| `sp_table_describe` | `Database: string`, `Name: string` | Returns column and index metadata. |
| `sp_table_drop` | `Database: string`, `Name: string` | Drops a table. |
| `sp_table_list` | `Database: string` | Lists tables (excludes `_sys_*` / provider system tables). |
| `sp_table_rename` | `Database: string`, `Name: string`, `NewName: string` | Renames a table. |
| `sp_dump` | `Database: string` | Returns full schema as a markdown document. |

## Columns

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_column_add` | `Database: string`, `Table: string`, `Name: string`, `Type: string`, `Nullable: bool` | Adds a column. |
| `sp_column_drop` | `Database: string`, `Table: string`, `Name: string` | Drops a column. |
| `sp_column_list` | `Database: string`, `Table: string` | Lists columns with metadata. |
| `sp_column_rename` | `Database: string`, `Table: string`, `Name: string`, `NewName: string` | Renames a column. |

## Indexes

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_index_create` | `Database: string`, `Table: string`, `Name: string`, `Columns: string`, `Unique: bool` | Creates an index. |
| `sp_index_drop` | `Database: string`, `Name: string` | Drops an index. |
| `sp_index_list` | `Database: string`, `Table: string` | Lists indexes on a table. |

## Data

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_select` | `Database: string`, `Table: string`, `Where?: string`, `OrderBy?: string`, `Limit: int`, `Offset: int` | Queries rows. `Where` is a JSON filter (see [QueryFilterBuilder](/reference/smartdata-server/#queryfilterbuilder)). |
| `sp_insert` | `Database: string`, `Table: string`, `Values: string` | Inserts a row from JSON. Returns last-inserted id. |
| `sp_update` | `Database: string`, `Table: string`, `Where: string`, `Set: string` | Updates matching rows. |
| `sp_delete` | `Database: string`, `Table: string`, `Where: string` | Deletes matching rows. `Where` is required. |
| `sp_data_export` | `Database: string`, `Table: string`, `Where?: string` | Exports rows as JSON. |
| `sp_data_import` | `Database: string`, `Table: string`, `Rows: string`, `Mode?: string`, `Truncate: bool`, `DryRun: bool` | Transactional bulk import. Modes: `insert` (default, fail on dup), `skip`, `replace`. |

## Backup & restore

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_backup_create` | `Databases: string` | Submits async create job. Comma-separated db names or `*`. Returns `JobId` + `BackupId`. |
| `sp_backup_restore` | `BackupId: string`, `Force: bool` | Submits async restore job. Returns `JobId`. |
| `sp_backup_status` | `JobId: string` | Polls `Status`, `Progress` (0–1), `ProgressMessage`, `Size`, `ElapsedMs`, `Error`. |
| `sp_backup_cancel` | `JobId: string` | Cancels a running job. |
| `sp_backup_list` | — | Lists backups via sidecar manifests. |
| `sp_backup_download` | `BackupId: string`, `Offset: long`, `ChunkSize: int` | Chunked download. |
| `sp_backup_upload` | `BackupId: string`, `Data: byte[]`, `Offset: long`, `TotalSize: long` | Chunked upload. |
| `sp_backup_drop` | `BackupId: string` | Deletes archive + sidecar. |
| `sp_backup_history` | — | Operation history (one JSON file per event). |

See [Backup storage layout](/reference/smartdata-server/#backup-storage-layout).

## Users & permissions

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_user_create` | `Username: string`, `Password: string` | Creates a user in master. |
| `sp_user_get` | `UserId: string` | User details with permissions. |
| `sp_user_list` | — | Lists users. |
| `sp_user_update` | `UserId: string`, `Username?: string`, `Password?: string`, `IsAdmin?: bool`, `IsDisabled?: bool` | Updates user. Disabling revokes active sessions. |
| `sp_user_delete` | `UserId: string` | Deletes non-admin user; revokes sessions. |
| `sp_user_permission_grant` | `UserId: string`, `PermissionKey: string` | Grants permission. |
| `sp_user_permission_revoke` | `UserId: string`, `PermissionKey: string` | Revokes permission. |
| `sp_user_permission_list` | `UserId: string` | Lists user permissions. |

## Settings

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_settings_list` | — | All settings (key, value, section, read-only, modified). |
| `sp_settings_update` | `Key: string`, `Value: string` | Updates a runtime-tunable setting (persists to `_sys_settings` + in-memory). Startup-only (`SchemaMode`, `Index.*`) not tunable. |

## Logging & telemetry

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_logs` | `Limit: int` | Recent `_sys_logs` entries (default 50). |
| `sp_errors` | `Name?: string`, `Limit: int` | Error / procedure-compilation logs. |
| `sp_storage` | `Database?: string` | Database + backup sizes. |
| `sp_metrics` | `Name?: string`, `Type?: string`, `Source?: string`, `From?: DateTime`, `To?: DateTime`, `Page: int`, `PageSize: int` | Counters / histograms / gauges. `Source` = `live` or `db`. |
| `sp_traces` | `TraceId?: string`, `Procedure?: string`, `Source?: string`, `ErrorsOnly?: bool`, `MinDurationMs?: double`, `From?: DateTime`, `To?: DateTime`, `Page: int`, `PageSize: int` | Distributed traces grouped into spans. |
| `sp_exceptions` | `ExceptionType?: string`, `Procedure?: string`, `Source?: string`, `From?: DateTime`, `To?: DateTime`, `Page: int`, `PageSize: int` | Captured exceptions with context. |

## Tracking & history

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_entity_history` | `Database: string`, `Table: string`, `Pk: string`, `Limit: int`, `Offset: int` | Version history for one entity (default 100). |
| `sp_schema_history` | `Database: string`, `Table: string` | Schema change timeline from ledger or sidecar. |
| `sp_history_prune` | `Database: string`, `Table: string`, `OlderThan: DateTime` | Deletes history rows older than timestamp. |
| `sp_tracking_drop` | `Database: string`, `Table: string`, `Confirm: string` | Drops all tracking (history + ledger) for an entity. |
| `sp_ledger_digest` | `Database: string`, `Table: string` | Captures current chain-head digest for anchoring. |
| `sp_ledger_verify` | `Database: string`, `Table: string`, `Anchors?: List<LedgerDigest>` | Verifies ledger chain integrity against optional anchors. |
| `sp_ledger_prune` | `Database: string`, `Table: string`, `OlderThan: DateTime` | Deletes ledger/history rows with chain-integrity verification. |
| `sp_ledger_drop` | `Database: string`, `Table: string`, `Confirm: string` | Drops ledger, keeps history (downgrade to plain tracking). |

Concepts: [Fundamentals → Tracking](/fundamentals/tracking/). How-tos: [Enable change tracking](/how-to/enable-change-tracking/), [Query entity history](/how-to/query-entity-history/).

## Scheduling

| Procedure | Parameters | Description |
|-----------|-----------|-------------|
| `sp_schedule_list` | `Search?: string`, `Enabled?: bool` | Paginated list with last-run outcome. |
| `sp_schedule_get` | `Id: int`, `RecentRuns: int` | One schedule + recent runs + `[Job]` metadata. |
| `sp_schedule_update` | `Id: int`, `Enabled?: bool`, `RetryAttempts?: int`, `RetryIntervalSeconds?: int`, `JitterSeconds?: int` | Updates user-controllable fields only. Timing is owned by code. |
| `sp_schedule_delete` | `Id: int` | Removes schedule + its runs. Attribute-sourced schedules reappear on next startup. |
| `sp_schedule_preview` | `Id: int`, `Count: int` | Next N fire times via `SlotComputer` (default 10). |
| `sp_schedule_history` | `ScheduleId?: int`, `ProcedureName?: string`, `Outcome?: string`, `Since?: DateTime`, `Until?: DateTime`, `Limit: int` | Run history with optional filters (default 100). |
| `sp_schedule_stats` | `WindowHours: int` | Counts + per-procedure avg duration over the window. |
| `sp_schedule_start` | `Id: int` | Manually triggers a run — claims a `SysScheduleRun` and queues `sp_schedule_execute`. |
| `sp_schedule_cancel` | `ScheduleId?: int`, `RunId?: long` | Flips `CancelRequested = true` on in-flight runs. |
| `sp_schedule_execute` | `RunId: long` | **[internal]** Executes one pre-claimed run; heartbeat + cancel watcher; retry-on-fail. |
| `sp_scheduler_tick` | — | **[internal]** Four-step pump: due → claim + queue, bounded catch-up, pending retries, orphan sweep. |
| `sp_schedule_run_retention` | — | **[internal]** Built-in `[Daily("03:15")]` job trimming `_sys_schedule_runs` older than `HistoryRetentionDays`. |

Concepts: [Fundamentals → Scheduling](/fundamentals/scheduling/). How-to: [Schedule a recurring job](/how-to/schedule-a-recurring-job/).
