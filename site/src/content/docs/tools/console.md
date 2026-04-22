# SmartData.Console

Embedded MVC-based admin console for database inspection, user management, backups, and data operations. Built on ASP.NET Core with Razor views, HTMX for dynamic interactions, and Tailwind CSS. Routes under `/{RoutePrefix}/` (default `/console/`).

## Project Structure

```
SmartData.Console/
├── Controllers/
│   ├── ConsoleBaseController.cs   Base: Exec<T>, IsHtmx, PageOrPartial, PopulateLayout
│   ├── AuthController.cs          Login/logout (/console/login)
│   ├── ConsoleController.cs       Dashboard home (/console)
│   ├── DatabaseController.cs      Database details, tables, backups (/console/db/{db})
│   ├── TableController.cs         Data grid, schema, query, export, import (/console/db/{db}/tables/{table})
│   ├── UsersController.cs         User CRUD + permissions (/console/users)
│   ├── SystemController.cs        Backups, logs, storage, procedures, metrics, exceptions, traces
│   ├── SchedulerController.cs     Schedule list/detail/history/stats + toggle/start/cancel/edit (/console/schedulers)
│   └── SettingsController.cs      View/edit SmartDataOptions (/console/settings)
├── Models/
│   ├── LayoutViewModel.cs         Sidebar: CurrentPath, CurrentDb, Databases, Username
│   ├── DatabaseViewModel.cs       DB info: Name, Size, Tables, Backups, ActiveTab
│   ├── DataGridViewModel.cs       Table data: Rows, Columns, Offset, Limit, OrderBy, Search
│   ├── SchemaViewModel.cs         Columns + Indexes for a table
│   ├── QueryViewModel.cs          Query builder: ColumnNames, ColumnTypes
│   ├── QueryResultViewModel.cs    Query results: Rows, Columns, Filter, Error
│   ├── ExportViewModel.cs         Export page: Db, Table
│   ├── ImportViewModel.cs         Import page + ImportPreviewViewModel + ImportResultViewModel
│   ├── BackupsViewModel.cs        Backups + Databases + History + ActiveJob + messages
│   ├── UsersViewModel.cs          User list
│   ├── UserEditViewModel.cs       User edit: permissions, roles, admin flag
│   ├── SystemPageViewModel.cs     Stub pages: Icon, Title, Description
│   ├── LogsViewModel.cs           Log entries + filters (type, procedure, search)
│   ├── StorageViewModel.cs        Storage stats: DB sizes, backup sizes, totals
│   ├── ProceduresViewModel.cs     Procedure list + ProcedureInfo + ProcedureParameter
│   ├── DashboardViewModel.cs      Dashboard: databases, metrics, exceptions, storage
│   ├── MetricsViewModel.cs        Metrics: counters, histograms, gauges + filters
│   ├── ExceptionsViewModel.cs     Exceptions list + filters (type, procedure)
│   ├── TracesViewModel.cs         Traces list + TraceDetailViewModel (spans)
│   ├── SchedulerViewModels.cs     SchedulerListViewModel, SchedulerDetailViewModel, SchedulerHistoryViewModel, SchedulerStatsViewModel
│   └── SettingsViewModel.cs       Settings groups + items for settings page
├── Views/
│   ├── Shared/
│   │   ├── _Layout.cshtml         Master layout: sidebar + #content area
│   │   ├── _Sidebar.cshtml        Database list + system nav + user footer
│   │   ├── _SidebarLink.cshtml    Reusable nav link with active state
│   │   ├── _Alert.cshtml          Alert component (error/success/warning/info)
│   │   ├── _Breadcrumbs.cshtml    Database → Tables → Table chain
│   │   └── _Tabs.cshtml           Tab navigation (data, schema, query, export, import)
│   ├── Auth/
│   │   └── Login.cshtml           Standalone login page
│   ├── Console/
│   │   └── Index.cshtml           Dashboard: health, metrics, exceptions, databases
│   ├── Database/
│   │   ├── Index.cshtml           DB details + tables + backups tabs
│   │   └── Empty.cshtml           No-tables empty state
│   ├── Table/
│   │   ├── TableData.cshtml       Data grid page with search
│   │   ├── Schema.cshtml          Column + index definitions
│   │   ├── Query.cshtml           Visual query builder
│   │   ├── Export.cshtml           Export download page
│   │   ├── Import.cshtml           JSON file upload
│   │   ├── _DataGrid.cshtml       Table with sortable headers + infinite scroll
│   │   ├── _Rows.cshtml           Table rows (initial load)
│   │   ├── _MoreRows.cshtml       Table rows (infinite scroll append)
│   │   ├── _FilterRow.cshtml      Query builder filter row
│   │   ├── _QueryResults.cshtml   Query result table
│   │   ├── _ImportPreview.cshtml  Column mapping + mode selection
│   │   ├── _ImportError.cshtml    Import error alert
│   │   └── _ImportResult.cshtml   Import success summary
│   ├── System/
│   │   ├── Backups.cshtml         Create/upload panels + tabs (backups/history)
│   │   ├── Stub.cshtml            Placeholder page (unused stubs)
│   │   ├── Logs.cshtml            Filterable system log viewer
│   │   ├── _LogsTable.cshtml      Log entries table partial
│   │   ├── Storage.cshtml         Storage overview: sizes, bars, tables
│   │   ├── Procedures.cshtml      Procedure listing with parameters + types
│   │   ├── Metrics.cshtml         Metrics dashboard: overview, counters, histograms, gauges
│   │   ├── Exceptions.cshtml      Exception list with expandable detail panels
│   │   ├── Traces.cshtml          Trace list with filters
│   │   ├── TraceDetail.cshtml     Single trace: summary + span tree + errors
│   │   ├── _SpanTree.cshtml       Recursive span hierarchy partial
│   │   ├── _BackupsTable.cshtml   Backup list with download/restore/delete actions
│   │   ├── _BackupHistory.cshtml  Operation history table
│   │   ├── _BackupJobProgress.cshtml  Self-polling progress bar (HTMX every 1s)
│   │   └── _BackupUpload.cshtml   Chunked upload UI with progress
│   ├── Settings/
│   │   └── Index.cshtml           Settings view/edit page (grouped by section)
│   ├── Scheduler/
│   │   ├── Index.cshtml           Schedule list with filter/search
│   │   ├── Detail.cshtml          Schedule detail wrapper
│   │   ├── _ScheduleTable.cshtml  List table partial (HTMX-swapped on toggle)
│   │   ├── _DetailBody.cshtml     Detail body (info + edit + preview + recent runs — HTMX-swapped)
│   │   ├── History.cshtml         Global run history
│   │   └── Stats.cshtml           Counters + per-procedure durations
│   └── Users/
│       ├── Index.cshtml           User list page
│       ├── Edit.cshtml            User edit: account, role, permissions
│       └── _UsersTable.cshtml     User table with actions
├── Services/
│   ├── ConsoleAuthService.cs      Session management: login, logout, token validation
│   └── PasswordHasher.cs          PBKDF2-SHA256 (100k iterations, 16-byte salt)
├── Middleware/
│   └── ConsoleAuthMiddleware.cs   Cookie-based session auth for all /{prefix}/* routes
├── wwwroot/js/
│   ├── query-builder.js           Visual filter builder: addFilter, buildFilterJson, castValue
│   └── backup-upload.js           Chunked upload (1MB): selectFile, cancel, reset, retry
├── ConsoleOptions.cs              Options: AllowInProduction (default false), RoutePrefix (default "console")
├── ConsoleRoutes.cs               Singleton exposing normalized Prefix + Path() helper for controllers/views
├── ConsoleRoutePrefixConvention.cs IApplicationModelConvention: rewrites /console route templates at startup
├── ServiceCollectionExtensions.cs AddSmartDataConsole() registration
└── WebApplicationExtensions.cs    UseSmartDataConsole() middleware pipeline
```

## Registration

```csharp
// Services
services.AddSmartDataConsole(options =>
{
    options.AllowInProduction = false; // default — disabled outside Development
    options.RoutePrefix = "console";  // default — routes at /console/...
});

// Middleware
app.UseSmartDataConsole(); // registers auth middleware only

// Endpoints (host is responsible for these)
app.MapStaticAssets();
app.MapControllers();
```

Registers `ConsoleAuthService` (singleton), `ConsoleRoutes` (singleton), `ConsoleOptions`, and MVC controllers from the Console assembly. An `IApplicationModelConvention` rewrites all controller route templates from `/console/...` to `/{RoutePrefix}/...` at startup. Disabled in non-Development environments unless `AllowInProduction = true`.

The host app must call `MapStaticAssets()` and `MapControllers()` to serve Console's static files and MVC routes. This allows Console to coexist with other frameworks (e.g., Blazor Server) without pipeline conflicts.

### Standalone API Setup

```csharp
app.UseSmartData();
app.UseSmartDataConsole();
app.MapStaticAssets();
app.MapControllers();
app.Run();
```

### Blazor Server Setup (Single Process)

```csharp
app.UseSmartData();
app.UseSmartDataConsole();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
```

## Routes

### Authentication

| Route | Method | Description |
|-------|--------|-------------|
| `/console/login` | GET | Login form (standalone, no layout) |
| `/console/login` | POST | Authenticate via `sp_login` → set `sd_console_token` HttpOnly cookie |
| `/console/logout` | POST | Clear session + cookie, call `sp_logout` |

### Home / Dashboard

| Route | Method | Description |
|-------|--------|-------------|
| `/console` | GET | Dashboard: health summary, metrics, recent exceptions, database overview |

### Database

| Route | Method | Description |
|-------|--------|-------------|
| `/console/db/{db}` | GET | Database details tab (name, size, table count) |
| `/console/db/{db}/tables` | GET | Tables list tab (name, rows, columns) |
| `/console/db/{db}/backups` | GET | Database-scoped backups tab |

### Table Data

| Route | Method | Query Params | Description |
|-------|--------|--------------|-------------|
| `/console/db/{db}/tables/{table}` | GET | limit, offset, orderBy, search | Data grid with sortable columns |
| `/console/db/{db}/tables/{table}/grid` | GET | limit, offset, orderBy, search | Grid HTML partial (HTMX swap) |
| `/console/db/{db}/tables/{table}/rows` | GET | limit, offset, orderBy, search | More rows for infinite scroll |

### Table Schema & Query

| Route | Method | Description |
|-------|--------|-------------|
| `/console/db/{db}/tables/{table}/schema` | GET | Column definitions + indexes |
| `/console/db/{db}/tables/{table}/query` | GET | Visual query builder form |
| `/console/db/{db}/tables/{table}/query/execute` | POST | Execute filter/sort query via `sp_select` |

### Table Export & Import

| Route | Method | Description |
|-------|--------|-------------|
| `/console/db/{db}/tables/{table}/export` | GET | Export form |
| `/console/db/{db}/tables/{table}/export/download` | GET | Download all rows as JSON |
| `/console/db/{db}/tables/{table}/import` | GET | Import form (JSON file upload) |
| `/console/db/{db}/tables/{table}/import/preview` | POST | Preview: column mapping, row count |
| `/console/db/{db}/tables/{table}/import/run` | POST | Execute import (insert/skip/replace + optional truncate) |

### Users

| Route | Method | Description |
|-------|--------|-------------|
| `/console/users` | GET | User list |
| `/console/users` | POST | Create user (inline) |
| `/console/users/new` | GET/POST | New user form / submit |
| `/console/users/{id}` | GET/POST | Edit user form / submit |
| `/console/users/{id}` | DELETE | Delete user |

### System — Backups

| Route | Method | Description |
|-------|--------|-------------|
| `/console/backups` | GET | Backup list page (two tabs: backups, history) |
| `/console/backups/history` | GET | Backup operation history tab |
| `/console/backups/create` | POST | Submit async create job → returns page with progress banner |
| `/console/backups/{id}` | DELETE | Delete backup |
| `/console/backups/{id}/restore` | POST | Submit async restore job → returns table with progress banner |
| `/console/backups/{id}/download` | GET | Download .smartbackup file |
| `/console/backups/upload` | POST | Chunked upload (JSON: backupId, data, offset, totalSize) |
| `/console/backups/jobs/{jobId}` | GET | Job status partial (polled by HTMX every 1s) |
| `/console/backups/jobs/{jobId}/cancel` | POST | Cancel running job |

### System — Settings

| Route | Method | Description |
|-------|--------|-------------|
| `/console/settings` | GET | View all SmartDataOptions (runtime + read-only) |
| `/console/settings` | POST | Save runtime-tunable settings to `_sys_settings` table + in-memory |

Settings are persisted as key-value rows in `_sys_settings` (master DB). On startup, `SettingsService.LoadFromDatabase()` applies persisted values to `SmartDataOptions`. Startup-only settings (SchemaMode, Index.*) display as read-only.

### System — Logs

| Route | Method | Query Params | Description |
|-------|--------|--------------|-------------|
| `/console/logs` | GET | type, procedure, search | Filterable system log viewer (from `sp_logs`) |

### System — Storage

| Route | Method | Description |
|-------|--------|-------------|
| `/console/storage` | GET | Storage overview: DB sizes, backup sizes, totals (from `sp_storage`) |

### System — Procedures

| Route | Method | Query Params | Description |
|-------|--------|--------------|-------------|
| `/console/procedures` | GET | tab | Registered procedures with parameters and return types (from `ProcedureCatalog`) |

### System — Metrics

| Route | Method | Query Params | Description |
|-------|--------|--------------|-------------|
| `/console/metrics` | GET | tab, name, source | Live/historical metrics: counters, histograms, gauges (from `sp_metrics`) |

### System — Exceptions

| Route | Method | Query Params | Description |
|-------|--------|--------------|-------------|
| `/console/exceptions` | GET | type, procedure | Exception list with expandable stack traces (from `sp_exceptions`) |

### System — Traces

| Route | Method | Query Params | Description |
|-------|--------|--------------|-------------|
| `/console/traces` | GET | procedure, errorsOnly, minDuration | Distributed trace list (from `sp_traces`) |
| `/console/traces/{traceId}` | GET | | Trace detail with span tree hierarchy |

### System — Schedulers

| Route | Method | Form/Query Params | Description |
|-------|--------|-------------------|-------------|
| `/console/schedulers` | GET | search, filter (all/enabled/disabled) | List of schedules with last outcome, inline enable-toggle and run-now buttons (from `sp_schedule_list`) |
| `/console/schedulers/{id}` | GET | | Schedule detail — timing, retry, next-fire preview, recent runs, edit form (from `sp_schedule_get` + `sp_schedule_preview`) |
| `/console/schedulers/{id}/toggle` | POST | enabled | Enable/disable schedule (→ `sp_schedule_update`) |
| `/console/schedulers/{id}/start` | POST | | Queue a manual run (→ `sp_schedule_start`) |
| `/console/schedulers/{id}/cancel` | POST | | Request cancel on in-flight runs (→ `sp_schedule_cancel`) |
| `/console/schedulers/{id}/toggle-detail` | POST | enabled | Enable/disable from the detail page (→ `sp_schedule_update`); returns `_DetailBody` partial |
| `/console/schedulers/{id}/edit` | POST | retryAttempts, retryIntervalSeconds, jitterSeconds | Update retry policy + jitter (→ `sp_schedule_update`) |
| `/console/schedulers/history` | GET | outcome, procedureName, limit | Global run log (from `sp_schedule_history`) |
| `/console/schedulers/stats` | GET | | 24h counters, currently running, pending retries, per-procedure avg duration (from `sp_schedule_stats`) |

## HTMX Patterns

All dynamic interactions use HTMX. The `ConsoleBaseController.PageOrPartial()` method returns a full page for regular requests or a partial for HTMX requests (detected via `HX-Request` header).

**Navigation:** `hx-get` + `hx-target="#content"` + `hx-push-url="true"` — swaps content area and updates browser URL.

**Forms:** `hx-post` + `hx-target` — submit forms without page reload, swap result into target.

**Infinite scroll:** Last table row has `hx-get="/rows?offset=N" hx-trigger="intersect once" hx-swap="afterend"`.

**Search debounce:** `hx-trigger="input changed delay:300ms"` on search input.

**Backup job polling:** `hx-get="/console/backups/jobs/{id}" hx-trigger="every 1s" hx-swap="outerHTML"` — self-replacing partial that stops polling when job completes (no trigger on terminal states).

## Authentication

Session-based authentication via `ConsoleAuthMiddleware`:

1. All `/console/*` routes require auth (except `/console/login` and static assets)
2. `sd_console_token` HttpOnly cookie holds a random 32-byte token
3. `ConsoleAuthService` maps console tokens to `ConsoleSession(Username, ServerToken, CreatedAt)`
4. Console sessions expire based on `SessionOptions.SessionTtl` (checked on access, no separate cleanup service)
5. Server token is passed to `Exec<T>()` for stored procedure authorization
6. Password hashing: PBKDF2-SHA256, 100k iterations, 16-byte random salt

## Client-Side JavaScript

**`query-builder.js`** — Visual filter builder for the query tab. Builds JSON filter from dynamic filter rows (column + operator + value). Supports operators: `$eq`, `$ne`, `$gt`, `$gte`, `$lt`, `$lte`, `$contains`, `$starts`, `$ends`, `$like`, `$in`, `$nin`, `$null`, `$notnull`. Casts values based on column types.

**`backup-upload.js`** — Chunked file upload (1MB chunks) with progress tracking. Base64-encodes chunks, POSTs to `/console/backups/upload`. Supports cancel and resume from last successful offset. Refreshes backup list via HTMX on completion.

## Styling

Tailwind CSS (CDN) with Material Symbols Outlined icons. Fonts: Inter (body), JetBrains Mono (code/data). Custom indigo-based color palette. All styling is utility-class-based in the views — no separate CSS files.
