namespace SmartData.Server.Providers;

/// <summary>
/// Read-only schema metadata. Replaces provider-specific introspection queries
/// (e.g. sqlite_master, PRAGMA, INFORMATION_SCHEMA).
/// All methods take a database name — the provider resolves connections internally.
/// </summary>
public interface ISchemaProvider
{
    bool TableExists(string dbName, string tableName);
    IEnumerable<ProviderColumnInfo> GetColumns(string dbName, string tableName);
    IEnumerable<ProviderTableInfo> GetTables(string dbName);
    IEnumerable<ProviderIndexInfo> GetIndexes(string dbName, string tableName);
    int GetRowCount(string dbName, string tableName);

    /// <summary>
    /// Returns table existence, columns, and indexes in a single call.
    /// Default implementation calls the individual methods (3 connections).
    /// Providers should override to use a single connection.
    /// </summary>
    TableSchemaSnapshot GetTableSchema(string dbName, string tableName)
    {
        if (!TableExists(dbName, tableName))
            return new TableSchemaSnapshot(false, [], []);

        var columns = GetColumns(dbName, tableName).ToList();
        var indexes = GetIndexes(dbName, tableName).ToList();
        return new TableSchemaSnapshot(true, columns, indexes);
    }
}
