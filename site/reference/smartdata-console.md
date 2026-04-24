---
title: SmartData.Console
description: Embedded admin console — routes, options, auth surface.
---

Embedded MVC admin console. ASP.NET Core + Razor + HTMX + Tailwind. Routes under `/{RoutePrefix}/` (default `/console/`). How-to: [Use the admin console](/how-to/use-the-admin-console/).

## Namespaces at a glance

| Folder | Contents |
|--------|----------|
| `Controllers/` | `ConsoleBase`, `Auth`, `Console`, `Database`, `Table`, `Users`, `System`, `Scheduler`, `Settings` |
| `Models/` | View models — dashboard, data grid, schema, query, backups, users, logs, storage, metrics, exceptions, traces, scheduler, settings |
| `Views/` | Razor views grouped by controller; shared layout/sidebar/breadcrumb/tabs partials |
| `Services/` | `ConsoleAuthService`, `PasswordHasher` (PBKDF2-SHA256, 100k iterations, 16-byte salt) |
| `Middleware/` | `ConsoleAuthMiddleware` — cookie-based session auth |
| `wwwroot/js/` | `query-builder.js`, `backup-upload.js` |

Routing is rewritten at startup via `ConsoleRoutePrefixConvention` (IApplicationModelConvention) — controller templates declared as `/console/...` become `/{RoutePrefix}/...`.

## Registration

```csharp
services.AddSmartDataConsole(o =>
{
    o.AllowInProduction = false;   // default — disabled outside Development
    o.RoutePrefix       = "console";
});

app.UseSmartDataConsole();        // auth middleware

// host is responsible:
app.MapStaticAssets();
app.MapControllers();
```

`AddSmartDataConsole` registers `ConsoleAuthService`, `ConsoleRoutes`, `ConsoleOptions`, and scans controllers from the Console assembly.

### ConsoleOptions

| Property | Default | Notes |
|----------|---------|-------|
| `AllowInProduction` | `false` | Disabled outside Development unless enabled explicitly. |
| `RoutePrefix` | `"console"` | Base path segment. |

## Routes

### Auth

| Route | Method | Notes |
|-------|--------|-------|
| `/console/login` | GET / POST | Login form; POST authenticates via `sp_login`, sets `sd_console_token` HttpOnly cookie. |
| `/console/logout` | POST | Clears cookie; calls `sp_logout`. |

### Dashboard

| Route | Method | Notes |
|-------|--------|-------|
| `/console` | GET | Health, metrics, recent exceptions, database overview. |

### Database

| Route | Method | Notes |
|-------|--------|-------|
| `/console/db/{db}` | GET | Details tab. |
| `/console/db/{db}/tables` | GET | Tables list tab. |
| `/console/db/{db}/backups` | GET | Database-scoped backups. |

### Tables

| Route | Method | Query | Notes |
|-------|--------|-------|-------|
| `/console/db/{db}/tables/{table}` | GET | limit, offset, orderBy, search | Data grid. |
| `/console/db/{db}/tables/{table}/grid` | GET | same | Grid partial (HTMX swap). |
| `/console/db/{db}/tables/{table}/rows` | GET | same | Infinite-scroll append partial. |
| `/console/db/{db}/tables/{table}/schema` | GET | — | Columns + indexes. |
| `/console/db/{db}/tables/{table}/query` | GET | — | Visual query builder. |
| `/console/db/{db}/tables/{table}/query/execute` | POST | — | Executes via `sp_select`. |
| `/console/db/{db}/tables/{table}/export` | GET | — | Export form. |
| `/console/db/{db}/tables/{table}/export/download` | GET | — | Download rows as JSON. |
| `/console/db/{db}/tables/{table}/import` | GET | — | JSON upload form. |
| `/console/db/{db}/tables/{table}/import/preview` | POST | — | Column mapping + row count. |
| `/console/db/{db}/tables/{table}/import/run` | POST | — | Insert/skip/replace + optional truncate. |

### Users

| Route | Method | Notes |
|-------|--------|-------|
| `/console/users` | GET / POST | List / inline create. |
| `/console/users/new` | GET / POST | Form. |
| `/console/users/{id}` | GET / POST / DELETE | Edit form / submit / delete. |

### Backups

| Route | Method | Notes |
|-------|--------|-------|
| `/console/backups` | GET | Tabs: backups, history. |
| `/console/backups/history` | GET | History tab. |
| `/console/backups/create` | POST | Submits async create job. |
| `/console/backups/{id}` | DELETE | Deletes backup. |
| `/console/backups/{id}/restore` | POST | Submits async restore. |
| `/console/backups/{id}/download` | GET | Streams `.smartbackup`. |
| `/console/backups/upload` | POST | Chunked upload (1MB). |
| `/console/backups/jobs/{jobId}` | GET | Status partial (HTMX polls every 1s). |
| `/console/backups/jobs/{jobId}/cancel` | POST | Cancels running job. |

### Settings

| Route | Method | Notes |
|-------|--------|-------|
| `/console/settings` | GET / POST | View / save runtime-tunable `SmartDataOptions`. Startup-only settings (`SchemaMode`, `Index.*`) render read-only. |

### Telemetry

| Route | Method | Query | Notes |
|-------|--------|-------|-------|
| `/console/logs` | GET | type, procedure, search | From `sp_logs`. |
| `/console/storage` | GET | — | From `sp_storage`. |
| `/console/procedures` | GET | tab | From `ProcedureCatalog`. |
| `/console/metrics` | GET | tab, name, source | Live + historical. From `sp_metrics`. |
| `/console/exceptions` | GET | type, procedure | From `sp_exceptions`. |
| `/console/traces` | GET | procedure, errorsOnly, minDuration | From `sp_traces`. |
| `/console/traces/{traceId}` | GET | — | Span tree. |

### Schedulers

| Route | Method | Notes |
|-------|--------|-------|
| `/console/schedulers` | GET | `sp_schedule_list`. Inline toggle + run-now. |
| `/console/schedulers/{id}` | GET | Detail — `sp_schedule_get` + `sp_schedule_preview`. |
| `/console/schedulers/{id}/toggle` | POST | `sp_schedule_update`. |
| `/console/schedulers/{id}/start` | POST | `sp_schedule_start`. |
| `/console/schedulers/{id}/cancel` | POST | `sp_schedule_cancel`. |
| `/console/schedulers/{id}/toggle-detail` | POST | `sp_schedule_update` → returns `_DetailBody`. |
| `/console/schedulers/{id}/edit` | POST | Updates retry + jitter. |
| `/console/schedulers/history` | GET | `sp_schedule_history`. |
| `/console/schedulers/stats` | GET | `sp_schedule_stats`. |

## HTMX conventions

`ConsoleBaseController.PageOrPartial()` returns a full page for standard requests and a partial when `HX-Request` is present.

| Pattern | Markup |
|---------|--------|
| Content navigation | `hx-get` + `hx-target="#content"` + `hx-push-url="true"` |
| Form submit | `hx-post` + `hx-target` |
| Infinite scroll | `hx-get="/rows?offset=N" hx-trigger="intersect once" hx-swap="afterend"` |
| Search debounce | `hx-trigger="input changed delay:300ms"` |
| Job polling | `hx-get="/console/backups/jobs/{id}" hx-trigger="every 1s" hx-swap="outerHTML"` (self-replacing; stops polling on terminal status) |

## Authentication

- `ConsoleAuthMiddleware` gates all `/console/*` routes except `/console/login` + static assets.
- Cookie `sd_console_token` — HttpOnly, random 32-byte token.
- `ConsoleAuthService` maps console token → `ConsoleSession(Username, ServerToken, CreatedAt)`.
- Session TTL from `SessionOptions.SessionTtl`. Expiration checked on access (no separate cleanup service).
- `ServerToken` is what's passed to `IAuthenticatedProcedureService` — real auth lives in `SessionManager`.

## Client-side JS

| File | Purpose |
|------|---------|
| `query-builder.js` | Builds JSON filter from dynamic rows. Operators: `$eq`, `$ne`, `$gt`, `$gte`, `$lt`, `$lte`, `$contains`, `$starts`, `$ends`, `$like`, `$in`, `$nin`, `$null`, `$notnull`. Casts by column type. |
| `backup-upload.js` | 1MB chunked upload to `/console/backups/upload`. Cancel + resume from last offset. Refreshes list via HTMX on completion. |

## Styling

Tailwind CDN + Material Symbols Outlined. Inter (body), JetBrains Mono (code/data). Utility classes only — no stylesheets.
