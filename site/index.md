---
title: SmartData
description: A .NET data framework with AutoRepo ORM, binary RPC, schema migration, and an embedded admin console.
---

Batteries-included .NET 10 data framework — typed stored procedures, auto-migrating schema, binary RPC, pluggable providers, and a built-in admin console.

- [Install](/get-started/install/)
- [Architecture](/overview/architecture/)
- [View on GitHub](https://github.com/codezerg/SmartData)

## Quick install

Add the SmartData NuGet feed, then install a package:

#### dotnet CLI

```bash
dotnet nuget add source https://smartdata-apis.netlify.app/nuget/v3/index.json --name SmartData
dotnet add package SmartData.Server.Sqlite
```

#### PowerShell

```powershell
dotnet nuget add source https://smartdata-apis.netlify.app/nuget/v3/index.json --name SmartData
dotnet add package SmartData.Server.Sqlite
```

#### NuGet.Config

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="SmartData" value="https://smartdata-apis.netlify.app/nuget/v3/index.json" />
  </packageSources>
</configuration>
```

See the [Install page](/get-started/install/) for the full package list.

## What's inside

### AutoRepo ORM

Auto-migrating schema, typed query API built on linq2db, and a convention-driven stored-procedure framework.

### Binary RPC

Compact binary protocol over a single `/rpc` endpoint. Typed client, typed server, shared DTOs.

### Pluggable providers

SQLite for local/dev, SQL Server for production. Swap via a single service registration.

### Admin console

Embedded MVC console at `/console/` — inspect tables, run queries, manage backups and schedules.

### Scheduling & Tracking

`[Daily]` / `[Every]` job attributes, plus opt-in change tracking and ledger for audit trails.

### CLI

`sd.exe` for database, table, column, index, data, import/export, backup, and procedure operations.
