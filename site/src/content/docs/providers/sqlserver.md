---
title: SmartData.Server.SqlServer
description: SQL Server database provider for SmartData.Server.
---

SQL Server database provider for SmartData.Server. Implements four provider interfaces using Microsoft.Data.SqlClient and T-SQL. Built on .NET 10.

## Dependencies

- **SmartData.Server** — Provider interfaces and core engine
- **Microsoft.Data.SqlClient** — SQL Server ADO.NET provider

## Registration

```csharp
builder.Services.AddSmartDataSqlServer(o =>
{
    o.ConnectionString = @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True";
});
```

Registers all four provider implementations as singletons:
- `SqlServerDatabaseProvider` → `IDatabaseProvider`
- `SqlServerSchemaProvider` → `ISchemaProvider`
- `SqlServerSchemaOperations` → `ISchemaOperations`
- `SqlServerRawDataProvider` → `IRawDataProvider`

## Configuration

```csharp
public class SqlServerDatabaseOptions
{
    public string ConnectionString { get; set; }  // Base connection string (no Initial Catalog)
    public string DataDirectory { get; set; }     // default: {AppBase}/data — for backups/exports
}
```

The `ConnectionString` should contain Server + authentication only. The provider appends `Initial Catalog` per database automatically.

## Project Structure

```
SmartData.Server.SqlServer/
├── SqlServerDatabaseProvider.cs      IDatabaseProvider — SQL Server instance management, master DB operations
├── SqlServerSchemaProvider.cs        ISchemaProvider — INFORMATION_SCHEMA + sys.* catalog views
├── SqlServerSchemaOperations.cs      ISchemaOperations — T-SQL DDL, IDENTITY, SQL Server type mapping
├── SqlServerRawDataProvider.cs       IRawDataProvider — SqlConnection for dynamic CRUD + raw SQL + OpenReader
├── SqlServerDatabaseOptions.cs       Configuration options
├── ServiceCollectionExtensions.cs    AddSmartDataSqlServer() extension method
└── SmartData.Server.SqlServer.csproj
```

## Implementation Details

### SqlServerDatabaseProvider
- Each "database" maps to a SQL Server database on the configured instance
- Connection strings built by appending `Initial Catalog={dbName}` to the base connection string
- `EnsureDatabase()` — creates database via `CREATE DATABASE` if it doesn't exist
- `DropDatabase()` — sets `SINGLE_USER WITH ROLLBACK IMMEDIATE` before dropping to close active connections
- `ListDatabases()` — queries `sys.databases WHERE database_id > 4` (excludes system databases)
- `GetDatabaseInfo()` — queries `sys.database_files` for size, `sys.databases` for creation date
- `DataDirectory` property exposes the working directory for backups/exports (used by `BackupService`)

### SqlServerSchemaProvider
- `GetTables()` — queries `INFORMATION_SCHEMA.TABLES` for table names + column counts, `COUNT(*)` for row counts
- `GetColumns()` — `INFORMATION_SCHEMA.COLUMNS` joined with `KEY_COLUMN_USAGE` for PK detection; `COLUMNPROPERTY(IsIdentity)` for identity columns
- `GetIndexes()` — queries `sys.indexes` + `sys.index_columns` + `sys.columns` with `STRING_AGG` for column lists; excludes primary key indexes
- `TableExists()` — `INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = ... AND TABLE_TYPE = 'BASE TABLE'`
- `GetTableSchema()` — batched single-connection override: runs TableExists + GetColumns + GetIndexes on one connection for SchemaManager validation

### SqlServerSchemaOperations
- Type mapping: string→NVARCHAR(MAX), int→INT, long→BIGINT, decimal→DECIMAL(18,4), double→FLOAT, bool→BIT, datetime→DATETIME2, guid→UNIQUEIDENTIFIER, byte[]→VARBINARY(MAX)
- `MapTypeReverse()` — reverse mapping for backup system portability. Handles parameterized types (e.g., `NVARCHAR(255)` → string) via contains-based fallback
- `CreateTable()` — with `IDENTITY(1,1)` for identity columns, named PK constraints (`PK_{tableName}`)
- `AlterColumn()` — native `ALTER TABLE ALTER COLUMN` (no copy strategy needed). Fills NULL values with defaults before changing nullable→non-nullable
- `AddColumn()` — uses temporary named default constraint for NOT NULL columns, then drops the constraint
- `RenameColumn()` / `RenameTable()` — uses `sp_rename`
- `DropColumn()` — drops any default constraints before dropping the column
- `DropIndex()` — looks up the owning table from `sys.indexes` (SQL Server requires `DROP INDEX ON table`)

### Full-Text Search
- `CreateFullTextIndex()` — Creates a shared fulltext catalog (`SmartDataFTCatalog`) with `IF NOT EXISTS` guard, then creates a `FULLTEXT INDEX` on the table using `KEY INDEX [PK_{table}]`
- `DropFullTextIndex()` — `DROP FULLTEXT INDEX ON [{table}]`
- `FullTextIndexExists()` — Queries `sys.fulltext_indexes` by `object_id`
- `BuildFullTextSearchSql()` — Returns: `SELECT TOP({limit}) t.* FROM [{table}] t WHERE CONTAINS(({columns}), @searchTerm)`
- Population is async (standard SQL Server behavior, resolves within seconds for small tables)

### SqlServerRawDataProvider
- Uses `SqlConnection` directly for dynamic queries (no linq2db entity mapping)
- `Select()` — parameterized WHERE with `OFFSET/FETCH NEXT` pagination (SQL Server requires `ORDER BY` for offset)
- `Insert()` — returns `SCOPE_IDENTITY()`
- `Import()` — transactional with three modes:
  - `"insert"` — standard INSERT (fails on conflict)
  - `"skip"` — uses `TRY/CATCH` to silently skip constraint violations (error 2627/2601)
  - `"replace"` — uses `MERGE` statement with primary key matching for upsert behavior
- `OpenReader()` — returns a streaming `IDataReader` over all rows, using `CommandBehavior.CloseConnection`
- `ExecuteRawSql()` — detects SELECT/WITH as queries vs DML. Returns `QueryResult` with columns, rows, or affected count
- `NotEqual` operator uses `<>` (ANSI standard) instead of `!=`
