namespace SmartData.Server.Providers;

/// <summary>
/// DDL execution. Each provider handles SQL generation and type mapping internally.
/// All methods take a database name — the provider resolves connections internally.
/// </summary>
public interface ISchemaOperations
{
    void CreateTable(string dbName, string name, IEnumerable<ColumnDefinition> columns);
    void DropTable(string dbName, string name);
    void RenameTable(string dbName, string name, string newName);
    void AddColumn(string dbName, string table, string columnName, string sqlType, bool nullable);
    void DropColumn(string dbName, string table, string columnName);
    void RenameColumn(string dbName, string table, string columnName, string newName);
    void CreateIndex(string dbName, string table, string indexName, string columns, bool unique);

    /// <summary>
    /// Creates an index with an optional filter predicate (filtered / partial
    /// index). SQLite and SQL Server both support <c>WHERE column IS NOT NULL</c>
    /// filters, which is what the ledger's "unique-where-not-null" constraint
    /// on <c>HistoryId</c> depends on. Default implementation ignores the filter
    /// for backwards compatibility; providers that support filtered indexes
    /// should override.
    /// </summary>
    void CreateIndex(string dbName, string table, string indexName, string columns, bool unique, string? whereClause)
        => CreateIndex(dbName, table, indexName, columns, unique);
    void DropIndex(string dbName, string indexName);
    void AlterColumn(string dbName, string table, string columnName, string newSqlType,
        bool newNullable, IEnumerable<ProviderColumnInfo> allColumns);

    /// <summary>
    /// Maps a logical type (string, int, decimal, bool, etc.) to the provider's SQL type.
    /// </summary>
    string MapType(string logicalType);

    /// <summary>
    /// Maps a logical type to the provider's SQL type, incorporating an optional column length.
    /// Providers that support sized types (e.g. NVARCHAR(N)) should use the length when provided.
    /// </summary>
    string MapType(string logicalType, int? length) => MapType(logicalType);

    /// <summary>
    /// Maps a provider-specific SQL type back to a logical type.
    /// Used by the backup system to store portable schema definitions.
    /// </summary>
    string MapTypeReverse(string sqlType);

    /// <summary>
    /// Returns the default value expression for a SQL type (e.g. "DEFAULT 0", "DEFAULT ''").
    /// Returns empty string for nullable columns.
    /// </summary>
    string GetDefaultValue(string sqlType, bool nullable);

    /// <summary>
    /// Creates a full-text search index on the specified columns.
    /// </summary>
    void CreateFullTextIndex(string dbName, string table, string[] columns)
        => throw new NotSupportedException("This provider does not support full-text indexes.");

    /// <summary>
    /// Drops the full-text search index on the specified table.
    /// </summary>
    void DropFullTextIndex(string dbName, string table)
        => throw new NotSupportedException("This provider does not support full-text indexes.");

    /// <summary>
    /// Checks whether a full-text search index exists on the specified table.
    /// </summary>
    bool FullTextIndexExists(string dbName, string table)
        => false;
}
