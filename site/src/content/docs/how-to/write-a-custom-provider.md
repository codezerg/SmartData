---
title: Write a custom provider
description: Implement the provider interfaces for a new database engine.
---

SmartData.Server depends only on a small set of provider interfaces. Ship a package that implements them and you can run SmartData against any engine LinqToDB supports.

## The interfaces

| Interface | Responsibility |
| --- | --- |
| **Database manager** | Create / drop / list databases, connection check, server version |
| **Schema inspector** | Enumerate tables, columns, indexes |
| **Schema mutator** | Create tables, add columns, create/drop indexes, full-text indexes |
| **Context factory** | Build a LinqToDB `DataConnection` bound to a logical database |

See `SmartData.Contracts` for the exact shapes.

## The skeleton

Start a new class library that references `SmartData.Contracts` and the engine-specific LinqToDB provider package (e.g. `linq2db.PostgreSQL`).

```csharp
// PostgresProvider.cs
public class PostgresDatabaseManager : IDatabaseManager { /* ... */ }
public class PostgresSchemaInspector : ISchemaInspector { /* ... */ }
public class PostgresSchemaMutator   : ISchemaMutator   { /* ... */ }
public class PostgresContextFactory  : IContextFactory  { /* ... */ }

public class PostgresOptions
{
    public string ConnectionString { get; set; } = "";
}

public static class PostgresServiceCollectionExtensions
{
    public static IServiceCollection AddSmartDataPostgres(
        this IServiceCollection services,
        Action<PostgresOptions> configure)
    {
        var options = new PostgresOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<IDatabaseManager,  PostgresDatabaseManager>();
        services.AddSingleton<ISchemaInspector,  PostgresSchemaInspector>();
        services.AddSingleton<ISchemaMutator,    PostgresSchemaMutator>();
        services.AddScoped<IContextFactory,      PostgresContextFactory>();

        return services;
    }
}
```

## What you actually have to write

Much of the work is already done by LinqToDB — it generates SQL, handles parameter binding, and knows the dialects. What a provider adds on top:

1. **Connection-string shaping.** Given a logical database name, produce a connection string. For SQL Server this is `Database={name}` replacement; for Postgres it's similar.
2. **DDL dialect.** `CREATE DATABASE`, `CREATE TABLE`, `ALTER TABLE ADD COLUMN`, `CREATE INDEX`, full-text equivalents — spell them out in the engine's dialect.
3. **Information-schema queries.** List tables, columns (name, type, nullability, max length), indexes. Most engines have `INFORMATION_SCHEMA.*` or a `pg_catalog`-style equivalent.
4. **Backup + restore.** Engine-native. Postgres → `pg_dump`/`pg_restore`; MySQL → `mysqldump`; etc. The interfaces let you shell out.

Use the existing SQLite and SQL Server providers as working templates — both are under `src/` in the main repo.

## Testing a provider

There's no formal conformance harness yet. The practical test: build a small app that registers your provider, defines a few entities with indexes and full-text, runs a few procedures (list / get / save / delete), toggles `[Tracked]`, runs a scheduled job, takes a backup. If all of that works on a fresh database, the provider is sound enough for most apps.

## Shipping

Two files at minimum:
- `SmartData.Server.{YourEngine}.csproj` — the library
- `{YourEngine}ServiceCollectionExtensions.cs` — public surface

Publish to the same feed you use for the rest of SmartData, or to nuget.org under your own namespace.

## Related

- [Providers](/fundamentals/providers/) — the provider model
- [SmartData.Server.Sqlite source](https://github.com/codezerg/SmartData/tree/main/src/SmartData.Server.Sqlite) — smallest reference implementation
- [SmartData.Server.SqlServer source](https://github.com/codezerg/SmartData/tree/main/src/SmartData.Server.SqlServer) — production-oriented implementation
