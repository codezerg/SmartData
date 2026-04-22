using Microsoft.Data.SqlClient;
using SmartData.Server.Providers;

namespace SmartData.Server.SqlServer;

public class SqlServerSchemaProvider : ISchemaProvider
{
    private readonly SqlServerDatabaseProvider _dbProvider;

    internal SqlServerSchemaProvider(SqlServerDatabaseProvider dbProvider)
    {
        _dbProvider = dbProvider;
    }

    public bool TableExists(string dbName, string tableName)
    {
        try
        {
            using var conn = _dbProvider.OpenDbConnection(dbName);
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
            using var conn = _dbProvider.OpenDbConnection(dbName);
            return ReadColumns(conn, tableName);
        }
        catch
        {
            return [];
        }
    }

    public IEnumerable<ProviderTableInfo> GetTables(string dbName)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                t.TABLE_NAME,
                (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_NAME = t.TABLE_NAME AND c.TABLE_SCHEMA = t.TABLE_SCHEMA) AS ColumnCount
            FROM INFORMATION_SCHEMA.TABLES t
            WHERE t.TABLE_TYPE = 'BASE TABLE' AND t.TABLE_SCHEMA = 'dbo'
            ORDER BY t.TABLE_NAME";

        var tables = new List<(string name, int columnCount)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
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
        using var conn = _dbProvider.OpenDbConnection(dbName);
        return ReadIndexes(conn, tableName);
    }

    public TableSchemaSnapshot GetTableSchema(string dbName, string tableName)
    {
        try
        {
            using var conn = _dbProvider.OpenDbConnection(dbName);

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

    private static bool ReadTableExists(SqlConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME = @name AND TABLE_TYPE = 'BASE TABLE'";
        cmd.Parameters.Add(new SqlParameter("@name", tableName));
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static List<ProviderColumnInfo> ReadColumns(SqlConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                c.COLUMN_NAME,
                c.DATA_TYPE,
                CASE WHEN c.IS_NULLABLE = 'YES' THEN 1 ELSE 0 END AS IsNullable,
                CASE WHEN kcu.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS IsPrimaryKey,
                COLUMNPROPERTY(OBJECT_ID(@schema + '.' + @table), c.COLUMN_NAME, 'IsIdentity') AS IsIdentity
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                ON tc.TABLE_NAME = c.TABLE_NAME AND tc.TABLE_SCHEMA = c.TABLE_SCHEMA AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            LEFT JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu
                ON kcu.CONSTRAINT_NAME = tc.CONSTRAINT_NAME AND kcu.TABLE_SCHEMA = tc.TABLE_SCHEMA AND kcu.COLUMN_NAME = c.COLUMN_NAME
            WHERE c.TABLE_NAME = @table AND c.TABLE_SCHEMA = 'dbo'
            ORDER BY c.ORDINAL_POSITION";
        cmd.Parameters.Add(new SqlParameter("@table", tableName));
        cmd.Parameters.Add(new SqlParameter("@schema", "dbo"));

        using var reader = cmd.ExecuteReader();
        var results = new List<ProviderColumnInfo>();
        while (reader.Read())
        {
            results.Add(new ProviderColumnInfo(
                Name: reader.GetString(0),
                Type: reader.GetString(1).ToUpperInvariant(),
                IsNullable: reader.GetInt32(2) == 1,
                IsPrimaryKey: reader.GetInt32(3) == 1,
                IsIdentity: Convert.ToInt32(reader.GetValue(4)) == 1
            ));
        }
        return results;
    }

    private static List<ProviderIndexInfo> ReadIndexes(SqlConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                i.name AS IndexName,
                i.is_unique AS IsUnique,
                STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.object_id = OBJECT_ID(@table)
                AND i.type > 0
                AND i.is_primary_key = 0
                AND i.name NOT LIKE 'PK_%'
            GROUP BY i.name, i.is_unique";
        cmd.Parameters.Add(new SqlParameter("@table", tableName));

        using var reader = cmd.ExecuteReader();
        var results = new List<ProviderIndexInfo>();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            var isUnique = reader.GetBoolean(1);
            var columns = reader.GetString(2);
            results.Add(new ProviderIndexInfo(name, null, columns, isUnique));
        }
        return results;
    }

    public int GetRowCount(string dbName, string tableName)
    {
        try
        {
            using var conn = _dbProvider.OpenDbConnection(dbName);
            return GetRowCountInternal(conn, tableName);
        }
        catch
        {
            return 0;
        }
    }

    private static int GetRowCountInternal(SqlConnection conn, string tableName)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM [{tableName}]";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }
}
