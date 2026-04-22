using LinqToDB.Data;

namespace SmartData.Server.Providers;

/// <summary>
/// Root database provider. Each database provider (SQLite, SQL Server, etc.)
/// implements this interface and exposes sub-providers for schema, data, and backup.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>
    /// Opens a new DataConnection for the named database, fully initialized
    /// with provider-specific settings (e.g. WAL pragma for SQLite).
    /// Caller is responsible for disposing.
    /// </summary>
    DataConnection OpenConnection(string dbName);

    /// <summary>
    /// Ensures the named database exists. Creates it if it doesn't.
    /// </summary>
    void EnsureDatabase(string dbName);

    /// <summary>
    /// Drops (deletes) the named database.
    /// </summary>
    void DropDatabase(string dbName);

    /// <summary>
    /// Lists all database names managed by this provider.
    /// </summary>
    IEnumerable<string> ListDatabases();

    /// <summary>
    /// Checks whether the named database exists.
    /// </summary>
    bool DatabaseExists(string dbName);

    /// <summary>
    /// Returns metadata for a database (size, created, modified).
    /// </summary>
    DatabaseInfo GetDatabaseInfo(string dbName);

    /// <summary>
    /// Read-only schema metadata (tables, columns, indexes).
    /// </summary>
    ISchemaProvider Schema { get; }

    /// <summary>
    /// DDL execution (create/drop/rename tables, columns, indexes).
    /// </summary>
    ISchemaOperations SchemaOperations { get; }

    /// <summary>
    /// Dynamic CRUD and raw SQL execution.
    /// </summary>
    IRawDataProvider RawData { get; }

    /// <summary>
    /// The root directory where databases and backups are stored.
    /// </summary>
    string DataDirectory { get; }

    /// <summary>
    /// Builds a provider-specific SQL query for full-text search on the given table and columns.
    /// </summary>
    string BuildFullTextSearchSql(string table, string[] columns, int limit)
        => throw new NotSupportedException("This provider does not support full-text search.");
}
