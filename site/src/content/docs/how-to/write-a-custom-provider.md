---
title: Write a custom provider
description: Implement the provider interfaces for a new database engine.
---

SmartData.Server depends on a single root provider interface (`IDatabaseProvider`) that exposes three sub-providers. Ship a package that implements them and you can run SmartData against any engine LinqToDB supports.

## The interfaces

All four live in `SmartData.Server.Providers` inside the `SmartData.Server` package:

| Interface | Responsibility |
| --- | --- |
| `IDatabaseProvider` | Root. Owns connection creation (`OpenConnection`), database lifecycle (`EnsureDatabase`, `DropDatabase`, `ListDatabases`, `DatabaseExists`, `GetDatabaseInfo`), `DataDirectory`, `BuildFullTextSearchSql`, and exposes the three sub-providers as properties. |
| `ISchemaProvider` | Read-only metadata — tables, columns, indexes, row counts. `GetTableSchema()` batches the common calls on one connection. |
| `ISchemaOperations` | DDL execution. Forward/reverse type mapping, create/alter/drop tables, columns, indexes; full-text create/drop/exists. |
| `IRawDataProvider` | Dynamic CRUD, import/export, raw SQL, streaming `OpenReader`. |

Only `IDatabaseProvider` is registered in DI. The three sub-providers are constructed by the root and reached via `provider.Schema`, `provider.SchemaOperations`, `provider.RawData`.

## The skeleton

Start a new class library referencing `SmartData.Server` and the engine-specific LinqToDB provider package (e.g. `linq2db.PostgreSQL`).

```csharp
// PostgresDatabaseProvider.cs
using LinqToDB.Data;
using Microsoft.Extensions.Options;
using SmartData.Server.Providers;

public class PostgresDatabaseProvider : IDatabaseProvider
{
    private readonly string _dataDirectory;
    private readonly string _baseConnectionString;

    public PostgresDatabaseProvider(IOptions<PostgresDatabaseOptions> options)
    {
        _dataDirectory        = options.Value.DataDirectory;
        _baseConnectionString = options.Value.ConnectionString;
        Directory.CreateDirectory(_dataDirectory);

        Schema           = new PostgresSchemaProvider(this);
        SchemaOperations = new PostgresSchemaOperations(this);
        RawData          = new PostgresRawDataProvider(this);
    }

    public ISchemaProvider   Schema           { get; }
    public ISchemaOperations SchemaOperations { get; }
    public IRawDataProvider  RawData          { get; }
    public string            DataDirectory    => _dataDirectory;

    public DataConnection OpenConnection(string dbName) { /* ... */ }
    public void EnsureDatabase(string dbName)           { /* CREATE DATABASE IF NOT EXISTS ... */ }
    public void DropDatabase(string dbName)             { /* ... */ }
    public IEnumerable<string> ListDatabases()          { /* ... */ }
    public bool DatabaseExists(string dbName)           { /* ... */ }
    public DatabaseInfo GetDatabaseInfo(string dbName)  { /* ... */ }

    public string BuildFullTextSearchSql(string table, string[] columns, int limit)
        => /* engine-specific — e.g. Postgres tsvector @@ plainto_tsquery */;
}

// Registration
public static class PostgresServiceCollectionExtensions
{
    public static IServiceCollection AddSmartDataPostgres(
        this IServiceCollection services,
        Action<PostgresDatabaseOptions>? configure = null)
    {
        services.Configure<PostgresDatabaseOptions>(o =>
        {
            o.DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        });
        if (configure != null)
            services.Configure(configure);

        services.AddSingleton<IDatabaseProvider, PostgresDatabaseProvider>();
        return services;
    }
}
```

## What you actually have to write

Much of the work is already done by LinqToDB — it generates SQL, handles parameter binding, and knows the dialects. What a provider adds on top:

1. **Connection-string shaping.** `OpenConnection(dbName)` must return a fully initialized `DataConnection` (including engine pragmas — e.g. SQLite sets `journal_mode=WAL`).
2. **Database lifecycle.** `EnsureDatabase`, `DropDatabase`, `ListDatabases`, `DatabaseExists`, `GetDatabaseInfo` — engine-specific. File-per-db for SQLite, `CREATE DATABASE` for SQL Server/Postgres.
3. **DDL dialect** (in `ISchemaOperations`). `CREATE TABLE`, `ALTER TABLE ADD COLUMN`, `CREATE INDEX`, full-text equivalents — spelled out in the engine's dialect. Forward type map (`MapType`) and reverse (`MapTypeReverse`, used by the backup system).
4. **Information-schema queries** (in `ISchemaProvider`). List tables, columns (name, type, nullability, PK, identity), indexes. Most engines have `INFORMATION_SCHEMA.*` or a `pg_catalog`-style equivalent.
5. **Raw data + full-text** (in `IRawDataProvider` and `BuildFullTextSearchSql`). Dynamic CRUD for the admin console, `OpenReader` for backup streaming, and the engine's native FTS syntax.

Use the existing SQLite and SQL Server providers as working templates — both are under `src/SmartData.Server.Sqlite/` and `src/SmartData.Server.SqlServer/` in the main repo.

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
