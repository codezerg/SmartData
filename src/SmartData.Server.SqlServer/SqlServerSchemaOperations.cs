using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SmartData.Server.Providers;

namespace SmartData.Server.SqlServer;

public class SqlServerSchemaOperations : ISchemaOperations
{
    private readonly SqlServerDatabaseProvider _dbProvider;
    private readonly ILogger _logger;

    internal SqlServerSchemaOperations(SqlServerDatabaseProvider dbProvider, ILogger logger)
    {
        _dbProvider = dbProvider;
        _logger = logger;
    }

    public void CreateTable(string dbName, string name, IEnumerable<ColumnDefinition> columns)
    {
        var cols = columns.ToList();
        var pkColumns = cols.Where(c => c.PrimaryKey).ToList();
        var colDefs = new List<string>();

        foreach (var col in cols)
        {
            var sqlType = MapType(col.Type, col.Length);

            if (col.PrimaryKey && sqlType.Contains("(MAX)", StringComparison.OrdinalIgnoreCase))
                throw new SmartDataException(
                    $"Column '{col.Name}' on table '{name}' is a string primary key but has no length specified. " +
                    $"SQL Server cannot index MAX-length columns. " +
                    $"Add [Column(Length = 450)], [MaxLength(450)], or [StringLength(450)] to the property.");

            var parts = new List<string> { $"[{col.Name}]", sqlType };

            if (col.Identity) parts.Add("IDENTITY(1,1)");
            if (!col.Nullable) parts.Add("NOT NULL");

            colDefs.Add(string.Join(" ", parts));
        }

        if (pkColumns.Count > 0)
            colDefs.Add($"CONSTRAINT [PK_{name}] PRIMARY KEY ({string.Join(", ", pkColumns.Select(c => $"[{c.Name}]"))})");

        using var conn = _dbProvider.OpenDbConnection(dbName);
        ExecuteSql(conn, $"CREATE TABLE [{name}] ({string.Join(", ", colDefs)})");
    }

    public void DropTable(string dbName, string name)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);
        ExecuteSql(conn, $"DROP TABLE [{name}]");
    }

    public void RenameTable(string dbName, string name, string newName)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);
        ExecuteSql(conn, $"EXEC sp_rename '{name}', '{newName}'");
    }

    public void AddColumn(string dbName, string table, string columnName, string sqlType, bool nullable)
    {
        var nullStr = nullable ? "NULL" : "NOT NULL";
        var defaultValue = GetDefaultValue(sqlType, nullable);

        using var conn = _dbProvider.OpenDbConnection(dbName);

        if (!nullable && defaultValue != "")
        {
            // Add with default constraint, then drop it (keeps column clean)
            var constraintName = $"DF_{table}_{columnName}";
            ExecuteSql(conn, $"ALTER TABLE [{table}] ADD [{columnName}] {sqlType} {nullStr} CONSTRAINT [{constraintName}]{defaultValue}");
            ExecuteSql(conn, $"ALTER TABLE [{table}] DROP CONSTRAINT [{constraintName}]");
        }
        else
        {
            ExecuteSql(conn, $"ALTER TABLE [{table}] ADD [{columnName}] {sqlType} {nullStr}");
        }
    }

    public void DropColumn(string dbName, string table, string columnName)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);

        // Drop any default constraints first
        ExecuteSql(conn, $@"
            DECLARE @constraint NVARCHAR(256)
            SELECT @constraint = dc.name
            FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID('{table}') AND c.name = '{columnName}'
            IF @constraint IS NOT NULL
                EXEC('ALTER TABLE [{table}] DROP CONSTRAINT [' + @constraint + ']')");

        ExecuteSql(conn, $"ALTER TABLE [{table}] DROP COLUMN [{columnName}]");
    }

    public void RenameColumn(string dbName, string table, string columnName, string newName)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);
        ExecuteSql(conn, $"EXEC sp_rename '{table}.{columnName}', '{newName}', 'COLUMN'");
    }

    public void CreateIndex(string dbName, string table, string indexName, string columns, bool unique)
        => CreateIndex(dbName, table, indexName, columns, unique, whereClause: null);

    public void CreateIndex(string dbName, string table, string indexName, string columns, bool unique, string? whereClause)
    {
        var uniqueStr = unique ? "UNIQUE " : "";
        var whereStr = string.IsNullOrEmpty(whereClause) ? "" : $" WHERE {whereClause}";
        using var conn = _dbProvider.OpenDbConnection(dbName);
        ExecuteSql(conn, $"CREATE {uniqueStr}INDEX [{indexName}] ON [{table}] ({columns}){whereStr}");
    }

    public void DropIndex(string dbName, string indexName)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);
        // Find the table that owns the index
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT OBJECT_NAME(object_id) FROM sys.indexes WHERE name = @name";
        cmd.Parameters.Add(new SqlParameter("@name", indexName));
        var tableName = cmd.ExecuteScalar() as string;

        if (tableName != null)
            ExecuteSql(conn, $"DROP INDEX [{indexName}] ON [{tableName}]");
    }

    public void AlterColumn(string dbName, string table, string columnName, string newSqlType,
        bool newNullable, IEnumerable<ProviderColumnInfo> allColumns)
    {
        var nullStr = newNullable ? "NULL" : "NOT NULL";
        using var conn = _dbProvider.OpenDbConnection(dbName);

        // When changing nullable → non-nullable, fill existing NULLs with a default value
        if (!newNullable)
        {
            var defaultValue = GetDefaultValue(newSqlType, false).Replace(" DEFAULT ", "");
            ExecuteSql(conn, $"UPDATE [{table}] SET [{columnName}] = {defaultValue} WHERE [{columnName}] IS NULL");
        }

        // Drop PK/default constraints that reference this column before altering
        var droppedPk = DropPrimaryKeyIfContains(conn, table, columnName);

        // When shrinking a PK column, verify no data would be truncated
        if (droppedPk != null)
        {
            var sizeMatch = System.Text.RegularExpressions.Regex.Match(newSqlType, @"\((\d+)\)");
            if (sizeMatch.Success)
            {
                var newSize = int.Parse(sizeMatch.Groups[1].Value);
                using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = $"SELECT MAX(LEN([{columnName}])) FROM [{table}]";
                var rawLen = checkCmd.ExecuteScalar();
                var maxLen = rawLen is DBNull or null ? 0 : Convert.ToInt32(rawLen);
                if (maxLen > newSize)
                    throw new SmartDataException(
                        $"Cannot shrink column '{columnName}' on table '{table}' from current max data length {maxLen} to {newSqlType}. " +
                        $"Existing data would be truncated.");
            }
        }
        DropDefaultConstraint(conn, table, columnName);

        // PK columns must be NOT NULL regardless of entity definition
        var effectiveNull = droppedPk != null ? "NOT NULL" : nullStr;
        ExecuteSql(conn, $"ALTER TABLE [{table}] ALTER COLUMN [{columnName}] {newSqlType} {effectiveNull}");

        // Re-add PK if we dropped it
        if (droppedPk != null)
            ExecuteSql(conn, $"ALTER TABLE [{table}] ADD CONSTRAINT [{droppedPk.Value.name}] PRIMARY KEY ({droppedPk.Value.columns})");
    }

    private static (string name, string columns)? DropPrimaryKeyIfContains(SqlConnection conn, string table, string columnName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.name AS PkName,
                   STRING_AGG(c.name, ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE i.object_id = OBJECT_ID(@table) AND i.is_primary_key = 1
            GROUP BY i.name
            HAVING MAX(CASE WHEN c.name = @col THEN 1 ELSE 0 END) = 1";
        cmd.Parameters.Add(new SqlParameter("@table", table));
        cmd.Parameters.Add(new SqlParameter("@col", columnName));

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var pkName = reader.GetString(0);
        var columns = reader.GetString(1);
        reader.Close();

        ExecuteSql(conn, $"ALTER TABLE [{table}] DROP CONSTRAINT [{pkName}]");
        return (pkName, string.Join(", ", columns.Split(',').Select(c => $"[{c.Trim()}]")));
    }

    private static void DropDefaultConstraint(SqlConnection conn, string table, string columnName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT dc.name FROM sys.default_constraints dc
            JOIN sys.columns c ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID(@table) AND c.name = @col";
        cmd.Parameters.Add(new SqlParameter("@table", table));
        cmd.Parameters.Add(new SqlParameter("@col", columnName));
        var constraintName = cmd.ExecuteScalar() as string;
        if (constraintName != null)
            ExecuteSql(conn, $"ALTER TABLE [{table}] DROP CONSTRAINT [{constraintName}]");
    }

    public void CreateFullTextIndex(string dbName, string table, string[] columns)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);

        // Check if the SQL Server instance supports full-text search
        if (!IsFullTextSupported(conn))
        {
            _logger.LogWarning("Full-text search is not available on this SQL Server instance. Skipping full-text index on [{Table}].", table);
            return;
        }

        // Ensure fulltext catalog exists
        ExecuteSql(conn, @"
            IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = 'SmartDataFTCatalog')
                CREATE FULLTEXT CATALOG [SmartDataFTCatalog]");

        // Create fulltext index on the table
        var colList = string.Join(", ", columns.Select(c => $"[{c}]"));
        ExecuteSql(conn, $"CREATE FULLTEXT INDEX ON [{table}] ({colList}) KEY INDEX [PK_{table}] ON [SmartDataFTCatalog]");
    }

    public void DropFullTextIndex(string dbName, string table)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);
        ExecuteSql(conn, $"DROP FULLTEXT INDEX ON [{table}]");
    }

    public bool FullTextIndexExists(string dbName, string table)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);

        if (!IsFullTextSupported(conn))
            return false;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(@table)";
        cmd.Parameters.Add(new SqlParameter("@table", table));
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static bool IsFullTextSupported(SqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT SERVERPROPERTY('IsFullTextInstalled')";
        return Convert.ToInt32(cmd.ExecuteScalar()) == 1;
    }

    public string MapType(string logicalType) => MapType(logicalType, null);

    public string MapType(string logicalType, int? length) => logicalType.ToLowerInvariant() switch
    {
        "int" => "INT",
        "long" => "BIGINT",
        "decimal" => "DECIMAL(18,4)",
        "double" => "FLOAT",
        "bool" => "BIT",
        "string" => length is > 0 ? $"NVARCHAR({length.Value})" : "NVARCHAR(MAX)",
        "datetime" => "DATETIME2",
        "guid" => "UNIQUEIDENTIFIER",
        "byte[]" => length is > 0 ? $"VARBINARY({length.Value})" : "VARBINARY(MAX)",
        _ => length is > 0 ? $"NVARCHAR({length.Value})" : "NVARCHAR(MAX)"
    };

    public string MapTypeReverse(string sqlType) => sqlType.ToUpperInvariant() switch
    {
        "INT" or "INTEGER" => "int",
        "BIGINT" => "long",
        "SMALLINT" or "TINYINT" => "int",
        "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY" => "decimal",
        "FLOAT" or "REAL" => "double",
        "BIT" => "bool",
        "NVARCHAR" or "VARCHAR" or "NCHAR" or "CHAR" or "TEXT" or "NTEXT" => "string",
        "DATETIME" or "DATETIME2" or "SMALLDATETIME" or "DATE" or "TIME" or "DATETIMEOFFSET" => "datetime",
        "UNIQUEIDENTIFIER" => "guid",
        "VARBINARY" or "BINARY" or "IMAGE" => "byte[]",
        _ => MapTypeReverseContains(sqlType)
    };

    private static string MapTypeReverseContains(string sqlType)
    {
        var upper = sqlType.ToUpperInvariant();
        if (upper.Contains("INT")) return "int";
        if (upper.Contains("CHAR") || upper.Contains("TEXT")) return "string";
        if (upper.Contains("DECIMAL") || upper.Contains("NUMERIC")) return "decimal";
        if (upper.Contains("FLOAT") || upper.Contains("REAL")) return "double";
        if (upper.Contains("DATE") || upper.Contains("TIME")) return "datetime";
        if (upper.Contains("BINARY")) return "byte[]";
        return "string";
    }

    public string GetDefaultValue(string sqlType, bool nullable)
    {
        if (nullable) return "";

        var upper = (sqlType ?? "").ToUpperInvariant();

        if (upper.Contains("INT") || upper.Contains("NUMERIC") || upper.Contains("DECIMAL") ||
            upper.Contains("FLOAT") || upper.Contains("REAL") || upper.Contains("MONEY"))
            return " DEFAULT 0";

        if (upper.Contains("BIT"))
            return " DEFAULT 0";

        if (upper.Contains("CHAR") || upper.Contains("TEXT"))
            return " DEFAULT ''";

        if (upper.Contains("DATE") || upper.Contains("TIME"))
            return " DEFAULT '0001-01-01'";

        if (upper.Contains("UNIQUEIDENTIFIER"))
            return " DEFAULT '00000000-0000-0000-0000-000000000000'";

        if (upper.Contains("BINARY"))
            return " DEFAULT 0x";

        return " DEFAULT ''";
    }

    private static void ExecuteSql(SqlConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
