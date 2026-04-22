---
title: Use the admin console
description: Mount and navigate the embedded Razor admin UI.
---

SmartData.Console is an embedded MVC + Razor admin UI. One `Add` + one `Map` call and it lives at `/console/` inside your app.

## Install + register

```bash
dotnet add package SmartData.Console
```

```csharp
using SmartData;

builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
builder.Services.AddSmartDataConsole();           // ← add this

var app = builder.Build();
app.UseSmartData();
app.UseSmartDataConsole();                        // ← and this
app.Run();
```

Open `http://localhost:5124/console/`. Log in with a valid SmartData user.

## What's inside

| Area | Purpose |
| --- | --- |
| **Dashboard** | Health, storage use, recent errors, active sessions |
| **Databases** | List, create, drop; switch the active database |
| **Tables** | Browse rows, inspect columns/indexes, run ad-hoc queries |
| **Procedures** | List registered procedures with their parameters; invoke manually |
| **Users & Permissions** | Create users, grant/revoke procedure permissions |
| **Schedulers** | Toggle schedules, run-now, cancel, read history and stats |
| **Backups** | Create, list, download, restore, drop |
| **Tracking** | Browse entity history; run ledger verify; view digests |
| **Logs & Errors** | Recent log lines and exceptions |
| **Settings** | Key/value settings backing `Setting`/`SettingValue` |

## Customising the mount path

```csharp
builder.Services.AddSmartDataConsole(o =>
{
    o.RoutePrefix = "admin";      // served at /admin/ instead of /console/
});
```

Pick a path that isn't claimed by your own controllers.

## Authentication

The console re-uses SmartData's user + session system — the same one user procedures authenticate against. Users log in through the console's own page, which calls `sp_login` and stores the session token in a cookie for subsequent requests.

There is **no anonymous access** to the console in any configuration. Reaching any protected page redirects to `/console/login`.

## Permissions

Each section of the console calls specific system procedures. A user's permission set controls what they can see:

- Read-only users get dashboards + browse views but hit "forbidden" on mutations.
- Admin users get everything.
- Custom roles are just permission presets — map them in `sp_user_permission_*`.

## Related

- [Register a provider](/how-to/register-a-provider/) — the `Program.cs` prerequisite
- [SmartData.Console reference](/reference/smartdata-console/) — full page-by-page detail
- [System procedures](/reference/system-procedures/) — what each console page calls
