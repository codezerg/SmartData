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

    /// <summary>
    /// SQLite has no native ALTER COLUMN for type / nullability — only ADD / DROP / RENAME.
    /// The official remediation is the 12-step table rebuild
    /// (https://sqlite.org/lang_altertable.html#otheralter): copy to temp table with
    /// the new shape, drop the old, rename. The trap is that a naive rebuild
    /// reconstructs each column definition from <see cref="ProviderColumnInfo"/>
    /// — which only carries name / type / nullable / pk / identity — and silently
    /// drops PRIMARY KEY inline syntax, AUTOINCREMENT, FOREIGN KEYs, CHECK,
    /// DEFAULT, COLLATE, GENERATED. We saw this the hard way: relaxing one
    /// orphan NOT NULL column stripped AUTOINCREMENT off the PK, breaking every
    /// subsequent insert.
    ///
    /// Fix: read the original CREATE TABLE SQL from <c>sqlite_master</c>, patch
    /// only the target column's type / nullability textually, and reuse the rest
    /// verbatim. Indexes and triggers are snapshotted and replayed after the
    /// rebuild. Whole thing runs inside a transaction with <c>foreign_keys=OFF</c>
    /// so a mid-step failure can't leave a half-rebuilt table behind.
    /// </summary>
    public void AlterColumn(string dbName, string table, string columnName, string newSqlType,
        bool newNullable, IEnumerable<ProviderColumnInfo> allColumns)
    {
        var columnNameList = allColumns.Select(c => $"[{c.Name}]").ToList();
        var columnsCsv = string.Join(", ", columnNameList);
        var tempTableName = $"{table}_temp_{Guid.NewGuid():N}";

        using var db = OpenConnection(dbName);

        var originalCreateSql = ReadSchemaSql(db, "table", table)
            ?? throw new InvalidOperationException(
                $"AlterColumn: no CREATE TABLE SQL found for '{table}' in sqlite_master.");

        var patchedCreateSql = RewriteCreateTable(
            originalCreateSql, tempTableName, columnName, newSqlType, newNullable);

        // Snapshot dependent objects — auto-created indexes (sqlite_autoindex_*)
        // have NULL sql and are rebuilt automatically by inline PRIMARY KEY /
        // UNIQUE in the CREATE TABLE we just patched, so the NOT NULL filter
        // here is correct.
        var savedIndexes = ReadObjectSqls(db, "index", table);
        var savedTriggers = ReadObjectSqls(db, "trigger", table);
        var ftsColumns = GetFtsColumns(db, table);

        // PRAGMA foreign_keys is a no-op inside a transaction; set before BEGIN.
        ExecuteSql(db, "PRAGMA foreign_keys=OFF");
        try
        {
            using var tx = db.BeginTransaction();
            try
            {
                ExecuteSql(db, patchedCreateSql, tx);
                ExecuteSql(db, $"INSERT INTO [{tempTableName}] ({columnsCsv}) SELECT {columnsCsv} FROM [{table}]", tx);
                ExecuteSql(db, $"DROP TABLE [{table}]", tx);
                ExecuteSql(db, $"ALTER TABLE [{tempTableName}] RENAME TO [{table}]", tx);

                foreach (var sql in savedIndexes) ExecuteSql(db, sql, tx);
                foreach (var sql in savedTriggers) ExecuteSql(db, sql, tx);
                if (ftsColumns != null) CreateFtsTriggers(db, table, ftsColumns, tx);

                CheckForeignKeyViolations(db, tx, table);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        finally
        {
            try { ExecuteSql(db, "PRAGMA foreign_keys=ON"); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Surgically rewrites one column's type + nullability inside an existing
    /// CREATE TABLE statement. All other syntax — PRIMARY KEY, AUTOINCREMENT,
    /// FOREIGN KEYs, CHECK, DEFAULT, COLLATE, table-level constraints — is
    /// preserved byte-for-byte. Also swaps the table name for <paramref name="newTableName"/>
    /// so the caller can stage the result under a temp name before rename.
    /// </summary>
    internal static string RewriteCreateTable(
        string originalSql, string newTableName,
        string targetColumn, string newType, bool newNullable)
    {
        var openIdx = originalSql.IndexOf('(');
        var closeIdx = originalSql.LastIndexOf(')');
        if (openIdx < 0 || closeIdx < 0 || closeIdx <= openIdx)
            throw new InvalidOperationException("CREATE TABLE SQL missing column-list parentheses.");

        var body = originalSql.Substring(openIdx + 1, closeIdx - openIdx - 1);
        var tail = originalSql.Substring(closeIdx + 1); // e.g. " WITHOUT ROWID"

        var segments = SplitTopLevel(body);
        var patched = false;
        for (var i = 0; i < segments.Count; i++)
        {
            var first = ReadFirstIdentifier(segments[i]);
            if (first != null && first.Equals(targetColumn, StringComparison.OrdinalIgnoreCase))
            {
                segments[i] = PatchColumnDefinition(segments[i], newType, newNullable);
                patched = true;
            }
        }

        if (!patched)
            throw new InvalidOperationException(
                $"AlterColumn: column '{targetColumn}' not found in CREATE TABLE body.");

        return $"CREATE TABLE [{newTableName}] ({string.Join(",", segments)}){tail}";
    }

    /// <summary>
    /// Splits a CREATE TABLE body on top-level commas, respecting nested
    /// parentheses, string literals ('…'), quoted identifiers ("…"), and
    /// bracketed identifiers ([…]).
    /// </summary>
    private static List<string> SplitTopLevel(string body)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        var inSingle = false;
        var inDouble = false;
        var inBracket = false;

        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (inSingle) { if (c == '\'') inSingle = false; continue; }
            if (inDouble) { if (c == '"') inDouble = false; continue; }
            if (inBracket) { if (c == ']') inBracket = false; continue; }
            switch (c)
            {
                case '\'': inSingle = true; break;
                case '"': inDouble = true; break;
                case '[': inBracket = true; break;
                case '(': depth++; break;
                case ')': depth--; break;
                case ',' when depth == 0:
                    parts.Add(body.Substring(start, i - start));
                    start = i + 1;
                    break;
            }
        }
        if (start < body.Length) parts.Add(body.Substring(start));
        return parts;
    }

    /// <summary>
    /// Reads the first identifier from a segment and strips surrounding quote /
    /// bracket characters. Returns null for table-level constraints that start
    /// with a reserved keyword (PRIMARY, UNIQUE, FOREIGN, CHECK, CONSTRAINT).
    /// </summary>
    private static string? ReadFirstIdentifier(string segment)
    {
        var i = 0;
        while (i < segment.Length && char.IsWhiteSpace(segment[i])) i++;
        if (i >= segment.Length) return null;

        string token;
        if (segment[i] == '[')
        {
            var end = segment.IndexOf(']', i);
            if (end < 0) return null;
            token = segment.Substring(i + 1, end - i - 1);
        }
        else if (segment[i] == '"')
        {
            var end = segment.IndexOf('"', i + 1);
            if (end < 0) return null;
            token = segment.Substring(i + 1, end - i - 1);
        }
        else
        {
            var start = i;
            while (i < segment.Length && !char.IsWhiteSpace(segment[i]) && segment[i] != '(')
                i++;
            token = segment.Substring(start, i - start);
        }

        // Skip table-level constraints.
        switch (token.ToUpperInvariant())
        {
            case "PRIMARY":
            case "UNIQUE":
            case "FOREIGN":
            case "CHECK":
            case "CONSTRAINT":
                return null;
        }
        return token;
    }

    /// <summary>
    /// Rewrites a single column-definition segment: preserve the name and every
    /// trailing constraint clause (PRIMARY KEY, AUTOINCREMENT, DEFAULT, CHECK,
    /// COLLATE, REFERENCES …), but swap the type and any existing
    /// NULL / NOT NULL specifier for the new values.
    /// </summary>
    private static string PatchColumnDefinition(string segment, string newType, bool newNullable)
    {
        var tokens = TokenizeColumnDef(segment);
        if (tokens.Count < 2) return segment; // malformed — leave as-is.

        var output = new List<string> { tokens[0], newType };
        var idx = 2;

        // Skip the original type's trailing size specifier e.g. "(10, 2)".
        if (idx < tokens.Count && tokens[idx].StartsWith("("))
            idx++;

        while (idx < tokens.Count)
        {
            var t = tokens[idx];
            var isNot = t.Equals("NOT", StringComparison.OrdinalIgnoreCase);
            if (isNot && idx + 1 < tokens.Count &&
                tokens[idx + 1].Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                idx += 2; // drop existing NOT NULL
                continue;
            }
            if (t.Equals("NULL", StringComparison.OrdinalIgnoreCase) &&
                (output.Count == 0 ||
                 !output[^1].Equals("DEFAULT", StringComparison.OrdinalIgnoreCase)))
            {
                idx++; // drop a standalone NULL that isn't part of `DEFAULT NULL`
                continue;
            }
            output.Add(t);
            idx++;
        }

        output.Add(newNullable ? "NULL" : "NOT NULL");
        return " " + string.Join(" ", output) + " ";
    }

    /// <summary>
    /// Tokenizes a column definition: identifiers (plain / [bracketed] / "quoted"),
    /// parenthesized groups, string literals, and everything else as words.
    /// Whitespace separates tokens but is not emitted.
    /// </summary>
    private static List<string> TokenizeColumnDef(string segment)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < segment.Length)
        {
            while (i < segment.Length && char.IsWhiteSpace(segment[i])) i++;
            if (i >= segment.Length) break;

            var start = i;
            var c = segment[i];

            if (c == '[')
            {
                while (i < segment.Length && segment[i] != ']') i++;
                if (i < segment.Length) i++;
            }
            else if (c == '"' || c == '\'')
            {
                var quote = c;
                i++;
                while (i < segment.Length && segment[i] != quote) i++;
                if (i < segment.Length) i++;
            }
            else if (c == '(')
            {
                var depth = 1;
                i++;
                while (i < segment.Length && depth > 0)
                {
                    if (segment[i] == '(') depth++;
                    else if (segment[i] == ')') depth--;
                    i++;
                }
            }
            else
            {
                while (i < segment.Length && !char.IsWhiteSpace(segment[i])
                       && segment[i] != '(' && segment[i] != '['
                       && segment[i] != '"' && segment[i] != '\'')
                    i++;
            }
            tokens.Add(segment.Substring(start, i - start));
        }
        return tokens;
    }

    private static string? ReadSchemaSql(SqliteConnection db, string type, string name)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type=@type AND name=@name";
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@name", name);
        return cmd.ExecuteScalar() as string;
    }

    private static List<string> ReadObjectSqls(SqliteConnection db, string type, string table)
    {
        var result = new List<string>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE type=@type AND tbl_name=@table AND sql IS NOT NULL";
        cmd.Parameters.AddWithValue("@type", type);
        cmd.Parameters.AddWithValue("@table", table);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(reader.GetString(0));
        return result;
    }

    private static void CheckForeignKeyViolations(SqliteConnection db, SqliteTransaction tx, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"PRAGMA foreign_key_check([{table}])";
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            throw new InvalidOperationException(
                $"AlterColumn on '{table}' produced foreign-key violations — rolled back.");
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

    private static void CreateFtsTriggers(SqliteConnection db, string table, string[] columns, SqliteTransaction? tx = null)
    {
        var ftsTable = $"{table}_fts";
        var colList = string.Join(", ", columns.Select(c => $"[{c}]"));
        var newCols = string.Join(", ", columns.Select(c => $"new.[{c}]"));
        var oldCols = string.Join(", ", columns.Select(c => $"old.[{c}]"));

        // AFTER INSERT
        ExecuteSql(db, $@"CREATE TRIGGER [{table}_fts_ai] AFTER INSERT ON [{table}] BEGIN
            INSERT INTO [{ftsTable}](rowid, {colList}) VALUES (new.[Id], {newCols});
        END", tx);

        // AFTER DELETE
        ExecuteSql(db, $@"CREATE TRIGGER [{table}_fts_ad] AFTER DELETE ON [{table}] BEGIN
            INSERT INTO [{ftsTable}]([{ftsTable}], rowid, {colList}) VALUES ('delete', old.[Id], {oldCols});
        END", tx);

        // AFTER UPDATE
        ExecuteSql(db, $@"CREATE TRIGGER [{table}_fts_au] AFTER UPDATE ON [{table}] BEGIN
            INSERT INTO [{ftsTable}]([{ftsTable}], rowid, {colList}) VALUES ('delete', old.[Id], {oldCols});
            INSERT INTO [{ftsTable}](rowid, {colList}) VALUES (new.[Id], {newCols});
        END", tx);
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

    private static void ExecuteSql(SqliteConnection conn, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (tx != null) cmd.Transaction = tx;
        cmd.ExecuteNonQuery();
    }
}
