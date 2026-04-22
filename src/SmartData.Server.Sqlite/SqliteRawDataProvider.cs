using System.Data;
using Microsoft.Data.Sqlite;
using SmartData.Contracts;
using SmartData.Server.Providers;

namespace SmartData.Server.Sqlite;

public class SqliteRawDataProvider : IRawDataProvider
{
    private readonly SqliteDatabaseProvider _dbProvider;

    internal SqliteRawDataProvider(SqliteDatabaseProvider dbProvider)
    {
        _dbProvider = dbProvider;
    }

    public IDataReader OpenReader(string dbName, string table)
    {
        var conn = OpenConnection(dbName);
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM [{table}]";
        return cmd.ExecuteReader(CommandBehavior.CloseConnection);
    }

    public List<Dictionary<string, object?>> Select(string dbName, string table,
        WhereClause? where, string? orderBy, int limit, int offset)
    {
        var (whereClause, parameters) = BuildWhere(where);
        var sql = $"SELECT * FROM [{table}] WHERE {whereClause}";

        if (!string.IsNullOrEmpty(orderBy))
        {
            var parts = orderBy.Split(':');
            var col = parts[0];
            var dir = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            sql += $" ORDER BY [{col}] {dir}";
        }

        if (limit > 0) sql += $" LIMIT {limit}";
        if (offset > 0) sql += $" OFFSET {offset}";

        using var conn = OpenConnection(dbName);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);

        using var reader = cmd.ExecuteReader();
        return ReadRows(reader);
    }

    public object Insert(string dbName, string table, Dictionary<string, object?> values)
    {
        using var conn = OpenConnection(dbName);
        using var cmd = conn.CreateCommand();
        var columns = new List<string>();
        var paramNames = new List<string>();
        var i = 0;

        foreach (var (col, val) in values)
        {
            var pName = $"@p{i++}";
            columns.Add($"[{col}]");
            paramNames.Add(pName);
            cmd.Parameters.AddWithValue(pName, val?.ToString() ?? (object)DBNull.Value);
        }

        cmd.CommandText = $"INSERT INTO [{table}] ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramNames)})";
        cmd.ExecuteNonQuery();

        using var idCmd = conn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        var lastId = idCmd.ExecuteScalar();

        return new { inserted = true, id = lastId };
    }

    public int Update(string dbName, string table, Dictionary<string, object?> setValues, WhereClause where)
    {
        var (whereClause, whereParams) = BuildWhere(where);

        using var conn = OpenConnection(dbName);
        using var cmd = conn.CreateCommand();
        var setParts = new List<string>();
        var setIdx = 0;

        foreach (var (col, val) in setValues)
        {
            var pName = $"@s{setIdx++}";
            setParts.Add($"[{col}] = {pName}");
            cmd.Parameters.AddWithValue(pName, val?.ToString() ?? (object)DBNull.Value);
        }

        AddParameters(cmd, whereParams);
        cmd.CommandText = $"UPDATE [{table}] SET {string.Join(", ", setParts)} WHERE {whereClause}";
        return cmd.ExecuteNonQuery();
    }

    public int Delete(string dbName, string table, WhereClause where)
    {
        var (whereClause, parameters) = BuildWhere(where);

        using var conn = OpenConnection(dbName);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM [{table}] WHERE {whereClause}";
        AddParameters(cmd, parameters);
        return cmd.ExecuteNonQuery();
    }

    public DataImportResult Import(string dbName, string table,
        List<Dictionary<string, object?>> rows, string mode, bool truncate)
    {
        var insertVerb = mode?.ToLowerInvariant() switch
        {
            "skip" => "INSERT OR IGNORE",
            "replace" => "INSERT OR REPLACE",
            _ => "INSERT"
        };

        using var conn = OpenConnection(dbName);
        using var tx = conn.BeginTransaction();
        var deleted = 0;
        var inserted = 0;
        var replaced = 0;
        var skipped = 0;

        if (truncate)
        {
            using var delCmd = conn.CreateCommand();
            delCmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            delCmd.Transaction = tx;
            deleted = Convert.ToInt32(delCmd.ExecuteScalar());

            using var truncCmd = conn.CreateCommand();
            truncCmd.CommandText = $"DELETE FROM [{table}]";
            truncCmd.Transaction = tx;
            truncCmd.ExecuteNonQuery();
        }

        var isReplace = insertVerb == "INSERT OR REPLACE";
        var countBefore = 0L;
        if (isReplace)
        {
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            countCmd.Transaction = tx;
            countBefore = Convert.ToInt64(countCmd.ExecuteScalar());
        }

        var processed = 0;
        foreach (var row in rows)
        {
            var columns = row.Keys.ToList();
            var colList = string.Join(", ", columns.Select(c => $"[{c}]"));
            var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"{insertVerb} INTO [{table}] ({colList}) VALUES ({paramList})";
            cmd.Transaction = tx;

            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", row[columns[i]] ?? DBNull.Value);

            var affected = cmd.ExecuteNonQuery();
            if (affected > 0)
                processed++;
            else
                skipped++;
        }

        if (isReplace)
        {
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            countCmd.Transaction = tx;
            var countAfter = Convert.ToInt64(countCmd.ExecuteScalar());
            inserted = (int)(countAfter - countBefore);
            replaced = processed - inserted;
        }
        else
        {
            inserted = processed;
        }

        tx.Commit();

        return new DataImportResult { Table = table, Inserted = inserted, Replaced = replaced, Skipped = skipped, Deleted = deleted };
    }

    public async Task<QueryResult> ExecuteRawSql(string dbName, string sql, CancellationToken ct)
    {
        await using var conn = OpenConnection(dbName);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var result = new QueryResult();

        if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            sql.TrimStart().StartsWith("PRAGMA", StringComparison.OrdinalIgnoreCase))
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            for (int i = 0; i < reader.FieldCount; i++)
                result.Columns.Add(reader.GetName(i));

            while (await reader.ReadAsync(ct))
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                result.Rows.Add(row);
            }
        }
        else
        {
            result.AffectedRows = await cmd.ExecuteNonQueryAsync(ct);
        }

        return result;
    }

    // --- SQL generation from WhereClause AST ---

    private static (string Sql, Dictionary<string, object> Params) BuildWhere(WhereClause? clause)
    {
        if (clause == null)
            return ("1=1", new());

        var parameters = new Dictionary<string, object>();
        var paramIndex = 0;
        var sql = EmitClause(clause, parameters, ref paramIndex);
        return (sql, parameters);
    }

    private static string EmitClause(WhereClause clause, Dictionary<string, object> parameters, ref int paramIndex)
    {
        switch (clause)
        {
            case Comparison c: return EmitComparison(c, parameters, ref paramIndex);
            case Like l: return EmitLike(l, parameters, ref paramIndex);
            case InList i: return EmitInList(i, parameters, ref paramIndex);
            case IsNull n: return $"[{n.Column}] IS {(n.Negate ? "NOT NULL" : "NULL")}";
            case And a: return EmitGroup(a.Conditions, " AND ", parameters, ref paramIndex);
            case Or o: return EmitGroup(o.Conditions, " OR ", parameters, ref paramIndex);
            default: return "1=1";
        }
    }

    private static string EmitGroup(WhereClause[] conditions, string connector, Dictionary<string, object> parameters, ref int paramIndex)
    {
        var parts = new List<string>();
        foreach (var condition in conditions)
            parts.Add(EmitClause(condition, parameters, ref paramIndex));
        return $"({string.Join(connector, parts)})";
    }

    private static string EmitComparison(Comparison c, Dictionary<string, object> parameters, ref int paramIndex)
    {
        var pName = $"@p{paramIndex++}";
        parameters[pName] = c.Value;
        var op = c.Op switch
        {
            CompareOp.Equal => "=",
            CompareOp.NotEqual => "!=",
            CompareOp.GreaterThan => ">",
            CompareOp.GreaterThanOrEqual => ">=",
            CompareOp.LessThan => "<",
            CompareOp.LessThanOrEqual => "<=",
            _ => "="
        };
        return $"[{c.Column}] {op} {pName}";
    }

    private static string EmitLike(Like l, Dictionary<string, object> parameters, ref int paramIndex)
    {
        var pName = $"@p{paramIndex++}";
        parameters[pName] = l.Pattern;
        return $"[{l.Column}] LIKE {pName}";
    }

    private static string EmitInList(InList i, Dictionary<string, object> parameters, ref int paramIndex)
    {
        var baseName = $"@p{paramIndex++}";
        var names = new List<string>();
        for (int j = 0; j < i.Values.Length; j++)
        {
            var name = $"{baseName}_{j}";
            parameters[name] = i.Values[j];
            names.Add(name);
        }
        var not = i.Negate ? "NOT " : "";
        return $"[{i.Column}] {not}IN ({string.Join(", ", names)})";
    }

    // --- Helpers ---

    private SqliteConnection OpenConnection(string dbName)
    {
        var conn = new SqliteConnection(_dbProvider.GetConnectionString(dbName));
        conn.Open();
        return conn;
    }

    private static void AddParameters(SqliteCommand cmd, Dictionary<string, object> parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
    }

    private static List<Dictionary<string, object?>> ReadRows(SqliteDataReader reader)
    {
        var results = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            results.Add(row);
        }
        return results;
    }

}
