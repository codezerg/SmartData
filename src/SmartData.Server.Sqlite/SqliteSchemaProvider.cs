using Microsoft.Data.Sqlite;
using SmartData.Server.Providers;

namespace SmartData.Server.Sqlite;

public class SqliteSchemaProvider : ISchemaProvider
{
    protected readonly SqliteDatabaseProvider _dbProvider;

    protected internal SqliteSchemaProvider(SqliteDatabaseProvider dbProvider)
    {
        _dbProvider = dbProvider;
    }

    public bool TableExists(string dbName, string tableName)
    {
        try
        {
            using var conn = OpenConnection(dbName);
            return ReadTableExists(conn, tableName);
        }
        catch
        {
            return false;
        }
    }

    public IEnumerable<ProviderColumnInfo> GetColumns(string dbName, string tableName)
    {
        try
        {
            using var conn = OpenConnection(dbName);
            return ReadColumns(conn, tableName);
        }
        catch
        {
            return [];
        }
    }

    public IEnumerable<ProviderTableInfo> GetTables(string dbName)
    {
        using var conn = OpenConnection(dbName);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                m.name,
                (SELECT COUNT(*) FROM pragma_table_info(m.name)) as columnCount
            FROM sqlite_master m
            WHERE m.type = 'table' AND m.name NOT LIKE 'sqlite_%'
                AND m.name NOT LIKE '%_fts' AND m.name NOT LIKE '%_fts_%'
            ORDER BY m.name
            """;
        using var reader = cmd.ExecuteReader();
        var tables = new List<(string name, int columnCount)>();
        while (reader.Read())
        {
            tables.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return tables.Select(t => new ProviderTableInfo(
            Name: t.name,
            ColumnCount: t.columnCount,
            RowCount: GetRowCountInternal(conn, t.name)
        )).ToList();
    }

    public IEnumerable<ProviderIndexInfo> GetIndexes(string dbName, string tableName)
    {
        using var conn = OpenConnection(dbName);
        return ReadIndexes(conn, tableName);
    }

    public TableSchemaSnapshot GetTableSchema(string dbName, string tableName)
    {
        try
        {
            using var conn = OpenConnection(dbName);

            if (!ReadTableExists(conn, tableName))
                return new TableSchemaSnapshot(false, [], []);

            var columns = ReadColumns(conn, tableName);
            var indexes = ReadIndexes(conn, tableName);
            return new TableSchemaSnapshot(true, columns, indexes);
        }
        catch
        {
            return new TableSchemaSnapshot(false, [], []);
        }
    }

    private static (string? Columns, bool IsUnique) ParseIndexSql(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return (null, false);

        var isUnique = sql.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);

        // Extract columns from "CREATE [UNIQUE] INDEX ... ON table (col1, col2)"
        var parenStart = sql.IndexOf('(');
        var parenEnd = sql.LastIndexOf(')');
        if (parenStart >= 0 && parenEnd > parenStart)
        {
            var columns = sql[(parenStart + 1)..parenEnd]
                .Replace("[", "").Replace("]", "").Trim();
            return (columns, isUnique);
        }

        return (null, isUnique);
    }

    public int GetRowCount(string dbName, string tableName)
    {
        try
        {
            using var conn = OpenConnection(dbName);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM [{tableName}]";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch
        {
            return 0;
        }
    }

    private static int GetRowCountInternal(SqliteConnection conn, string tableName)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM [{tableName}]";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    private static bool ReadTableExists(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static List<ProviderColumnInfo> ReadColumns(SqliteConnection conn, string tableName)
    {
        var hasAutoIncrement = false;
        using (var sqlCmd = conn.CreateCommand())
        {
            sqlCmd.CommandText = $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{tableName}'";
            var createSql = sqlCmd.ExecuteScalar() as string ?? "";
            hasAutoIncrement = createSql.Contains("AUTOINCREMENT", StringComparison.OrdinalIgnoreCase);
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{tableName}])";
        using var reader = cmd.ExecuteReader();
        var results = new List<ProviderColumnInfo>();
        while (reader.Read())
        {
            var isPk = reader.GetInt32(reader.GetOrdinal("pk")) > 0;
            results.Add(new ProviderColumnInfo(
                Name: reader.GetString(reader.GetOrdinal("name")),
                Type: reader.GetString(reader.GetOrdinal("type")),
                IsNullable: reader.GetInt32(reader.GetOrdinal("notnull")) == 0,
                IsPrimaryKey: isPk,
                IsIdentity: isPk && hasAutoIncrement
            ));
        }
        return results;
    }

    private static List<ProviderIndexInfo> ReadIndexes(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name, sql FROM sqlite_master WHERE type='index' AND tbl_name='{tableName}' AND name NOT LIKE 'sqlite_%'";
        using var reader = cmd.ExecuteReader();
        var results = new List<ProviderIndexInfo>();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var sql = reader.IsDBNull(1) ? null : reader.GetString(1);
            var (columns, isUnique) = ParseIndexSql(sql);
            results.Add(new ProviderIndexInfo(name, sql, columns, isUnique));
        }
        return results;
    }

    /// <summary>
    /// Opens a raw <see cref="SqliteConnection"/> for the named database.
    /// Override in an encrypted subclass to execute <c>PRAGMA key</c>
    /// immediately after <see cref="SqliteConnection.Open"/> and before any
    /// caller sees the connection.
    /// </summary>
    protected virtual SqliteConnection OpenConnection(string dbName)
    {
        var conn = new SqliteConnection(_dbProvider.GetConnectionString(dbName));
        conn.Open();
        return conn;
    }
}
