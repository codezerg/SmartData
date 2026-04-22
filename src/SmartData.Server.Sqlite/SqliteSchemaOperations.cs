using Microsoft.Data.Sqlite;
using SmartData.Server.Providers;

namespace SmartData.Server.Sqlite;

public class SqliteSchemaOperations : ISchemaOperations
{
    private readonly SqliteDatabaseProvider _dbProvider;

    internal SqliteSchemaOperations(SqliteDatabaseProvider dbProvider)
    {
        _dbProvider = dbProvider;
    }

    public void CreateTable(string dbName, string name, IEnumerable<ColumnDefinition> columns)
    {
        var cols = columns.ToList();
        var pkColumns = cols.Where(c => c.PrimaryKey).ToList();
        var colDefs = new List<string>();

        foreach (var col in cols)
        {
            var sqlType = MapType(col.Type);
            var parts = new List<string> { $"[{col.Name}]", sqlType };

            if (col.PrimaryKey && pkColumns.Count == 1)
            {
                parts.Add("PRIMARY KEY");
                if (col.Identity) parts.Add("AUTOINCREMENT");
            }

            if (!col.Nullable) parts.Add("NOT NULL");
            colDefs.Add(string.Join(" ", parts));
        }

        if (pkColumns.Count > 1)
            colDefs.Add($"PRIMARY KEY ({string.Join(", ", pkColumns.Select(c => $"[{c.Name}]"))})");

        using var db = OpenConnection(dbName);
        ExecuteSql(db, $"CREATE TABLE [{name}] ({string.Join(", ", colDefs)})");
    }

    public void DropTable(string dbName, string name)
    {
        using var db = OpenConnection(dbName);
        ExecuteSql(db, $"DROP TABLE [{name}]");
    }

    public void RenameTable(string dbName, string name, string newName)
    {
        using var db = OpenConnection(dbName);
        ExecuteSql(db, $"ALTER TABLE [{name}] RENAME TO [{newName}]");
    }

    public void AddColumn(string dbName, string table, string columnName, string sqlType, bool nullable)
    {
        var nullStr = nullable ? "NULL" : "NOT NULL";
        var defaultValue = GetDefaultValue(sqlType, nullable);
        using var db = OpenConnection(dbName);
        ExecuteSql(db, $"ALTER TABLE [{table}] ADD COLUMN [{columnName}] {sqlType} {nullStr}{defaultValue}");
    }

    public void DropColumn(string dbName, string table, string columnName)
    {
        using var db = OpenConnection(dbName);
        ExecuteSql(db, $"ALTER TABLE [{table}] DROP COLUMN [{columnName}]");
    }

    public void RenameColumn(string dbName, string table, string columnName, string newName)
    {
        using var db = OpenConnection(dbName);
        ExecuteSql(db, $"ALTER TABLE [{table}] RENAME COLUMN [{columnName}] TO [{newName}]");
    }

    public void CreateIndex(string dbName, string table, string indexName, string columns, bool unique)
        => CreateIndex(dbName, table, indexName, columns, unique, whereClause: null);

    public void CreateIndex(string dbName, string table, string indexName, string columns, bool unique, string? whereClause)
    {
        var uniqueStr = unique ? "UNIQUE " : "";
        var whereStr = string.IsNullOrEmpty(whereClause) ? "" : $" WHERE {whereClause}";
        using var db = OpenConnection(dbName);
        ExecuteSql(db, $"CREATE {uniqueStr}INDEX [{indexName}] ON [{table}] ({columns}){whereStr}");
    }

    public void DropIndex(string dbName, string indexName)
    {
        using var db = OpenConnection(dbName);
        ExecuteSql(db, $"DROP INDEX [{indexName}]");
    }

    public void AlterColumn(string dbName, string table, string columnName, string newSqlType,
        bool newNullable, IEnumerable<ProviderColumnInfo> allColumns)
    {
        var columns = allColumns.ToList();
        var tempTableName = $"{table}_temp_{Guid.NewGuid():N}";

        var columnDefs = columns.Select(c =>
        {
            var type = string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase) ? newSqlType : c.Type;
            var nullable = string.Equals(c.Name, columnName, StringComparison.OrdinalIgnoreCase) ? newNullable : c.IsNullable;
            return $"[{c.Name}] {type} {(nullable ? "NULL" : "NOT NULL")}";
        });

        using var db = OpenConnection(dbName);

        // Check if FTS table exists before alter (triggers will be destroyed by table recreation)
        var ftsColumns = GetFtsColumns(db, table);

        try
        {
            ExecuteSql(db, $"CREATE TABLE [{tempTableName}] ({string.Join(", ", columnDefs)})");
            var columnNames = string.Join(", ", columns.Select(c => $"[{c.Name}]"));
            ExecuteSql(db, $"INSERT INTO [{tempTableName}] ({columnNames}) SELECT {columnNames} FROM [{table}]");
            ExecuteSql(db, $"DROP TABLE [{table}]");
            ExecuteSql(db, $"ALTER TABLE [{tempTableName}] RENAME TO [{table}]");

            // Rebuild FTS triggers if they existed before the alter
            if (ftsColumns != null)
                CreateFtsTriggers(db, table, ftsColumns);
        }
        catch
        {
            try { ExecuteSql(db, $"DROP TABLE IF EXISTS [{tempTableName}]"); } catch { }
            throw;
        }
    }

    public void CreateFullTextIndex(string dbName, string table, string[] columns)
    {
        var colList = string.Join(", ", columns);
        var ftsTable = $"{table}_fts";

        using var db = OpenConnection(dbName);

        // 1. Create FTS5 virtual table (content-sync mode)
        ExecuteSql(db, $"CREATE VIRTUAL TABLE [{ftsTable}] USING fts5({colList}, content=[{table}], content_rowid=Id)");

        // 2. Populate FTS table from existing data
        var selectCols = string.Join(", ", columns.Select(c => $"[{c}]"));
        ExecuteSql(db, $"INSERT INTO [{ftsTable}](rowid, {selectCols}) SELECT [Id], {selectCols} FROM [{table}]");

        // 3. Create sync triggers
        CreateFtsTriggers(db, table, columns);
    }

    public void DropFullTextIndex(string dbName, string table)
    {
        using var db = OpenConnection(dbName);

        // Drop triggers first
        ExecuteSql(db, $"DROP TRIGGER IF EXISTS [{table}_fts_ai]");
        ExecuteSql(db, $"DROP TRIGGER IF EXISTS [{table}_fts_ad]");
        ExecuteSql(db, $"DROP TRIGGER IF EXISTS [{table}_fts_au]");

        // Drop FTS virtual table
        ExecuteSql(db, $"DROP TABLE IF EXISTS [{table}_fts]");
    }

    public bool FullTextIndexExists(string dbName, string table)
    {
        using var db = OpenConnection(dbName);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}_fts'";
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static void CreateFtsTriggers(SqliteConnection db, string table, string[] columns)
    {
        var ftsTable = $"{table}_fts";
        var colList = string.Join(", ", columns.Select(c => $"[{c}]"));
        var newCols = string.Join(", ", columns.Select(c => $"new.[{c}]"));
        var oldCols = string.Join(", ", columns.Select(c => $"old.[{c}]"));

        // AFTER INSERT
        ExecuteSql(db, $@"CREATE TRIGGER [{table}_fts_ai] AFTER INSERT ON [{table}] BEGIN
            INSERT INTO [{ftsTable}](rowid, {colList}) VALUES (new.[Id], {newCols});
        END");

        // AFTER DELETE
        ExecuteSql(db, $@"CREATE TRIGGER [{table}_fts_ad] AFTER DELETE ON [{table}] BEGIN
            INSERT INTO [{ftsTable}]([{ftsTable}], rowid, {colList}) VALUES ('delete', old.[Id], {oldCols});
        END");

        // AFTER UPDATE
        ExecuteSql(db, $@"CREATE TRIGGER [{table}_fts_au] AFTER UPDATE ON [{table}] BEGIN
            INSERT INTO [{ftsTable}]([{ftsTable}], rowid, {colList}) VALUES ('delete', old.[Id], {oldCols});
            INSERT INTO [{ftsTable}](rowid, {colList}) VALUES (new.[Id], {newCols});
        END");
    }

    /// <summary>
    /// Reads FTS column names from the FTS virtual table if it exists.
    /// Returns null if no FTS table exists for this table.
    /// </summary>
    private static string[]? GetFtsColumns(SqliteConnection db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT sql FROM sqlite_master WHERE type='table' AND name='{table}_fts'";
        var sql = cmd.ExecuteScalar() as string;
        if (sql == null)
            return null;

        // Parse columns from: CREATE VIRTUAL TABLE [x_fts] USING fts5(col1, col2, content=[x], content_rowid=Id)
        var start = sql.IndexOf("fts5(", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += 5;
        var end = sql.IndexOf(')', start);
        if (end < 0) return null;

        return sql[start..end]
            .Split(',')
            .Select(s => s.Trim())
            .Where(s => !s.StartsWith("content", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public string MapType(string logicalType) => logicalType.ToLowerInvariant() switch
    {
        "int" or "long" => "INTEGER",
        "decimal" or "double" => "REAL",
        "bool" => "INTEGER",
        "string" => "TEXT",
        "datetime" => "TEXT",
        "guid" => "TEXT",
        "byte[]" => "BLOB",
        _ => "TEXT"
    };

    public string MapTypeReverse(string sqlType) => sqlType.ToUpperInvariant() switch
    {
        "INTEGER" or "INT" or "BIGINT" or "SMALLINT" or "TINYINT" => "int",
        "REAL" or "FLOAT" or "DOUBLE" or "NUMERIC" or "DECIMAL" => "double",
        "TEXT" or "VARCHAR" or "NVARCHAR" or "CHAR" or "CLOB" => "string",
        "BLOB" => "byte[]",
        _ => "string"
    };

    public string GetDefaultValue(string sqlType, bool nullable)
    {
        if (nullable) return "";

        var upper = (sqlType ?? "").ToUpperInvariant();

        if (upper.Contains("INT") || upper.Contains("NUMERIC") || upper.Contains("REAL") ||
            upper.Contains("FLOAT") || upper.Contains("DOUBLE") || upper.Contains("DECIMAL"))
            return " DEFAULT 0";

        if (upper.Contains("TEXT") || upper.Contains("CHAR") || upper.Contains("CLOB") ||
            upper.Contains("VARCHAR") || upper.Contains("NVARCHAR"))
            return " DEFAULT ''";

        if (upper.Contains("BLOB"))
            return " DEFAULT X''";

        if (upper.Contains("BOOL"))
            return " DEFAULT 0";

        return " DEFAULT ''";
    }

    private SqliteConnection OpenConnection(string dbName)
    {
        var conn = new SqliteConnection(_dbProvider.GetConnectionString(dbName));
        conn.Open();
        return conn;
    }

    private static void ExecuteSql(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
