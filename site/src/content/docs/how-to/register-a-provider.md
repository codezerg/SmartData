---
title: Register a provider
description: Wire up SQLite or SQL Server at service-registration time.
---

One line in `Program.cs`, between `AddSmartData` and `AddStoredProcedures`.

## SQLite

```csharp
using SmartData;

builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
```

Databases are stored as `data/{name}.db` relative to the app. The first call to a database creates the file.

To change the data directory:

```csharp
builder.Services.AddSmartDataSqlite(o =>
{
    o.DataDirectory = "/var/lib/smartdata";
});
```

## SQL Server

```csharp
using SmartData;

builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlServer(o =>
{
    o.ConnectionString = "Server=localhost;Database=master;"
                       + "Trusted_Connection=true;TrustServerCertificate=true";
});
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
```

When the caller targets a logical database other than `master`, SmartData replaces the `Database=` in the connection string per call — you don't need one connection string per database.

For production, use environment-specific connection strings:

```csharp
builder.Services.AddSmartDataSqlServer(o =>
{
    o.ConnectionString = builder.Configuration.GetConnectionString("SmartData")!;
});
```

## Both providers in the same app? No.

A single app registers exactly one provider. If you need SQL Server in prod and SQLite locally, branch the registration:

```csharp
if (builder.Environment.IsDevelopment())
    builder.Services.AddSmartDataSqlite();
else
    builder.Services.AddSmartDataSqlServer(o => { /* ... */ });
```

## Mapping the endpoints

After any provider registration, map the RPC + health endpoints:

```csharp
var app = builder.Build();
app.UseSmartData();     // POST /rpc + GET /health
app.Run();
```

## Verify the wiring

Hit `/health`:

```bash
curl http://localhost:5124/health
```

You should get a JSON status with provider info and the list of databases the server knows about.

## Related

- [Providers](/fundamentals/providers/) — the provider model
- [SmartData.Server.Sqlite reference](/reference/smartdata-server-sqlite/)
- [SmartData.Server.SqlServer reference](/reference/smartdata-server-sqlserver/)
- [Write a custom provider](/how-to/write-a-custom-provider/) — for engines not listed here
