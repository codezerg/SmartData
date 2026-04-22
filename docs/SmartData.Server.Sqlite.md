# SmartData.Server.Sqlite

SQLite database provider for SmartData.Server. Implements four provider interfaces using Microsoft.Data.Sqlite and SQLite-specific SQL. Built on .NET 10.

## Dependencies

- **SmartData.Server** — Provider interfaces and core engine
- **Microsoft.Data.Sqlite** — SQLite ADO.NET provider

## Registration

```csharp
builder.Services.AddSmartDataSqlite();                         // defaults
builder.Services.AddSmartDataSqlite(o => o.DataDirectory = "data");  // custom path
```

Registers all four provider implementations as singletons:
- `SqliteDatabaseProvider` → `IDatabaseProvider`
- `SqliteSchemaProvider` → `ISchemaProvider`
- `SqliteSchemaOperations` → `ISchemaOperations`
- `SqliteRawDataProvider` → `IRawDataProvider`

## Configuration

```csharp
public class SqliteDatabaseOptions
{
    public string DataDirectory { get; set; }  // default: {AppBase}/data
}
```

## Project Structure

```
SmartData.Server.Sqlite/
├── SqliteDatabaseProvider.cs      IDatabaseProvider — file-based .db paths, WAL pragma, DataDirectory
├── SqliteSchemaProvider.cs        ISchemaProvider — sqlite_master + PRAGMA table_info()
├── SqliteSchemaOperations.cs      ISchemaOperations — DDL with AUTOINCREMENT, SQLite type names, MapTypeReverse
├── SqliteRawDataProvider.cs       IRawDataProvider — SqliteConnection for dynamic CRUD + raw SQL + OpenReader
├── SqliteDatabaseOptions.cs       Configuration options
├── ServiceCollectionExtensions.cs AddSmartDataSqlite() extension method
└── SmartData.Server.Sqlite.csproj
```

## Implementation Details

### SqliteDatabaseProvider
- Databases stored as `.db` files in `DataDirectory`
- Connection strings: `Data Source={DataDirectory}/{name}.db`
- `DataDirectory` property exposes the root data path (used by `BackupService`)
- `OpenConnection()` enables WAL journal mode
- `EnsureDatabase()` just ensures the directory exists (SQLite auto-creates on connect)

### SqliteSchemaProvider
- `GetTables()` — queries `sqlite_master` for table names + `pragma_table_info` for column counts, `COUNT(*)` for row counts
- `GetColumns()` — `PRAGMA table_info()` returning name, type, nullable, pk; detects AUTOINCREMENT from table SQL for `IsIdentity`
- `GetIndexes()` — queries `sqlite_master WHERE type='index'`; parses index SQL to populate `Columns` and `IsUnique`
- `TableExists()` — `SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=...`
- `GetTableSchema()` — batched single-connection override: runs TableExists + GetColumns + GetIndexes on one connection for SchemaManager validation

### SqliteSchemaOperations
- Type mapping: string→TEXT, int/long→INTEGER, decimal/double→REAL, bool→INTEGER, byte[]→BLOB, etc.
- `MapTypeReverse()` — reverse mapping: TEXT→string, INTEGER→int, REAL→decimal, BLOB→byte[], etc. Used by backup system for portable schema definitions
- `CreateTable()` — with AUTOINCREMENT for integer PKs
- `AlterColumn()` — SQLite doesn't support ALTER COLUMN, uses copy-and-rename strategy (create temp → copy data → drop original → rename temp). Detects and rebuilds FTS triggers after table recreation.
- `AddColumn()` — with appropriate default values for NOT NULL columns

### Full-Text Search (FTS5)
- `CreateFullTextIndex()` — Creates an FTS5 content-sync virtual table (`{table}_fts`) with 3 sync triggers:
  - `{table}_fts_ai` — AFTER INSERT: inserts new row into FTS table
  - `{table}_fts_ad` — AFTER DELETE: removes row from FTS table
  - `{table}_fts_au` — AFTER UPDATE: deletes old + inserts new in FTS table
- `DropFullTextIndex()` — Drops the 3 triggers and the FTS5 virtual table
- `FullTextIndexExists()` — Checks `sqlite_master` for `{table}_fts`
- `BuildFullTextSearchSql()` — Returns: `SELECT t.* FROM [table] t INNER JOIN [table_fts] fts ON t.Id = fts.rowid WHERE [table_fts] MATCH @searchTerm ORDER BY rank LIMIT {limit}`
- FTS shadow tables (`_fts`, `_fts_content`, `_fts_data`, `_fts_config`, `_fts_docsize`, `_fts_idx`) are filtered from `GetTables()`
- **Edge case:** `AlterColumn()` recreates the table via temp-copy which destroys triggers. The implementation detects existing FTS tables and rebuilds triggers after the alter.

### SqliteRawDataProvider
- Uses `SqliteConnection` directly for dynamic queries (no linq2db entity mapping)
- `Select()` — parameterized WHERE with LIMIT/OFFSET pagination
- `Insert()` — returns `last_insert_rowid()`
- `Import()` — transactional with INSERT OR IGNORE (skip) / INSERT OR REPLACE (replace) modes
- `OpenReader()` — returns a streaming `IDataReader` over all rows in a table, using `CommandBehavior.CloseConnection` so disposing the reader also closes the connection
- `ExecuteRawSql()` — for admin console ad-hoc queries
