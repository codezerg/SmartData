using System.Data;
using Microsoft.Data.SqlClient;
using SmartData.Contracts;
using SmartData.Server.Providers;

namespace SmartData.Server.SqlServer;

public class SqlServerRawDataProvider : IRawDataProvider
{
    private readonly SqlServerDatabaseProvider _dbProvider;

    internal SqlServerRawDataProvider(SqlServerDatabaseProvider dbProvider)
    {
        _dbProvider = dbProvider;
    }

    public IDataReader OpenReader(string dbName, string table)
    {
        var conn = _dbProvider.OpenDbConnection(dbName);
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM [{table}]";
        return cmd.ExecuteReader(CommandBehavior.CloseConnection);
    }

    public List<Dictionary<string, object?>> Select(string dbName, string table,
        WhereClause? where, string? orderBy, int limit, int offset)
    {
        var (whereClause, parameters) = BuildWhere(where);
        var sql = $"SELECT * FROM [{table}] WHERE {whereClause}";

        // SQL Server requires ORDER BY for OFFSET/FETCH
        var hasOrderBy = !string.IsNullOrEmpty(orderBy);
        if (hasOrderBy)
        {
            var parts = orderBy!.Split(':');
            var col = parts[0];
            var dir = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            sql += $" ORDER BY [{col}] {dir}";
        }

        if (offset > 0 || limit > 0)
        {
            if (!hasOrderBy)
                sql += " ORDER BY (SELECT NULL)";

            sql += $" OFFSET {offset} ROWS";
            if (limit > 0)
                sql += $" FETCH NEXT {limit} ROWS ONLY";
        }

        using var conn = _dbProvider.OpenDbConnection(dbName);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);

        using var reader = cmd.ExecuteReader();
        return ReadRows(reader);
    }

    public object Insert(string dbName, string table, Dictionary<string, object?> values)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);
        using var cmd = conn.CreateCommand();
        var columns = new List<string>();
        var paramNames = new List<string>();
        var i = 0;

        foreach (var (col, val) in values)
        {
            var pName = $"@p{i++}";
            columns.Add($"[{col}]");
            paramNames.Add(pName);
            cmd.Parameters.AddWithValue(pName, val ?? DBNull.Value);
        }

        cmd.CommandText = $"INSERT INTO [{table}] ({string.Join(", ", columns)}) VALUES ({string.Join(", ", paramNames)}); SELECT SCOPE_IDENTITY()";
        var lastId = cmd.ExecuteScalar();

        return new { inserted = true, id = lastId is DBNull ? 0 : lastId };
    }

    public int Update(string dbName, string table, Dictionary<string, object?> setValues, WhereClause where)
    {
        var (whereClause, whereParams) = BuildWhere(where);

        using var conn = _dbProvider.OpenDbConnection(dbName);
        using var cmd = conn.CreateCommand();
        var setParts = new List<string>();
        var setIdx = 0;

        foreach (var (col, val) in setValues)
        {
            var pName = $"@s{setIdx++}";
            setParts.Add($"[{col}] = {pName}");
            cmd.Parameters.AddWithValue(pName, val ?? DBNull.Value);
        }

        AddParameters(cmd, whereParams);
        cmd.CommandText = $"UPDATE [{table}] SET {string.Join(", ", setParts)} WHERE {whereClause}";
        return cmd.ExecuteNonQuery();
    }

    public int Delete(string dbName, string table, WhereClause where)
    {
        var (whereClause, parameters) = BuildWhere(where);

        using var conn = _dbProvider.OpenDbConnection(dbName);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM [{table}] WHERE {whereClause}";
        AddParameters(cmd, parameters);
        return cmd.ExecuteNonQuery();
    }

    public DataImportResult Import(string dbName, string table,
        List<Dictionary<string, object?>> rows, string mode, bool truncate)
    {
        using var conn = _dbProvider.OpenDbConnection(dbName);
        using var tx = conn.BeginTransaction();
        var deleted = 0;
        var inserted = 0;
        var replaced = 0;
        var skipped = 0;

        // Check if table has an identity column and if rows contain it
        var hasIdentity = false;
        if (rows.Count > 0)
        {
            var identityCol = GetIdentityColumn(conn, tx, table);
            if (identityCol != null && rows[0].ContainsKey(identityCol))
            {
                hasIdentity = true;
                using var idOn = conn.CreateCommand();
                idOn.Transaction = tx;
                idOn.CommandText = $"SET IDENTITY_INSERT [{table}] ON";
                idOn.ExecuteNonQuery();
            }
        }

        if (truncate)
        {
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            countCmd.Transaction = tx;
            deleted = Convert.ToInt32(countCmd.ExecuteScalar());

            using var truncCmd = conn.CreateCommand();
            truncCmd.CommandText = $"DELETE FROM [{table}]";
            truncCmd.Transaction = tx;
            truncCmd.ExecuteNonQuery();
        }

        var isReplace = mode?.Equals("replace", StringComparison.OrdinalIgnoreCase) == true;
        var isSkip = mode?.Equals("skip", StringComparison.OrdinalIgnoreCase) == true;

        var countBefore = 0L;
        if (isReplace)
        {
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            countCmd.Transaction = tx;
            countBefore = Convert.ToInt64(countCmd.ExecuteScalar());
        }

        foreach (var row in rows)
        {
            var columns = row.Keys.ToList();
            var colList = string.Join(", ", columns.Select(c => $"[{c}]"));
            var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            if (isSkip)
            {
                // Use TRY/CATCH to skip on conflict
                cmd.CommandText = $@"
                    BEGIN TRY
                        INSERT INTO [{table}] ({colList}) VALUES ({paramList})
                    END TRY
                    BEGIN CATCH
                        IF ERROR_NUMBER() NOT IN (2627, 2601) THROW
                    END CATCH";
            }
            else if (isReplace)
            {
                // Get primary key columns for MERGE
                var pkCols = GetPrimaryKeyColumns(conn, tx, table);
                if (pkCols.Count > 0)
                {
                    var matchCondition = string.Join(" AND ", pkCols.Select(pk => $"target.[{pk}] = source.[{pk}]"));
                    var sourceValues = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
                    var sourceColumns = string.Join(", ", columns.Select(c => $"[{c}]"));
                    var updateSet = string.Join(", ", columns.Where(c => !pkCols.Contains(c, StringComparer.OrdinalIgnoreCase)).Select(c => $"target.[{c}] = source.[{c}]"));

                    var mergeSql = $@"
                        MERGE [{table}] AS target
                        USING (SELECT {string.Join(", ", columns.Select((c, i) => $"@p{i} AS [{c}]"))}) AS source ({sourceColumns})
                        ON {matchCondition}";

                    if (!string.IsNullOrEmpty(updateSet))
                        mergeSql += $" WHEN MATCHED THEN UPDATE SET {updateSet}";

                    mergeSql += $" WHEN NOT MATCHED THEN INSERT ({colList}) VALUES ({string.Join(", ", columns.Select(c => $"source.[{c}]"))});";

                    cmd.CommandText = mergeSql;
                }
                else
                {
                    cmd.CommandText = $"INSERT INTO [{table}] ({colList}) VALUES ({paramList})";
                }
            }
            else
            {
                cmd.CommandText = $"INSERT INTO [{table}] ({colList}) VALUES ({paramList})";
            }

            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", row[columns[i]] ?? DBNull.Value);

            var affected = cmd.ExecuteNonQuery();
            if (affected > 0)
                inserted++;
            else if (isSkip)
                skipped++;
        }

        if (isReplace)
        {
            using var countCmd = conn.CreateCommand();
            countCmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
            countCmd.Transaction = tx;
            var countAfter = Convert.ToInt64(countCmd.ExecuteScalar());
            var newRows = (int)(countAfter - countBefore);
            replaced = inserted - newRows;
            inserted = newRows;
        }

        if (hasIdentity)
        {
            using var idOff = conn.CreateCommand();
            idOff.Transaction = tx;
            idOff.CommandText = $"SET IDENTITY_INSERT [{table}] OFF";
            idOff.ExecuteNonQuery();
        }

        tx.Commit();

        return new DataImportResult { Table = table, Inserted = inserted, Replaced = replaced, Skipped = skipped, Deleted = deleted };
    }

    private static string? GetIdentityColumn(SqlConnection conn, SqlTransaction tx, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT c.name
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(@table) AND c.is_identity = 1";
        cmd.Parameters.AddWithValue("@table", table);
        return cmd.ExecuteScalar() as string;
    }

    public async Task<QueryResult> ExecuteRawSql(string dbName, string sql, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_dbProvider.GetConnectionString(dbName));
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        var result = new QueryResult();

        if (sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            sql.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
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
            CompareOp.NotEqual => "<>",
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

    private static List<string> GetPrimaryKeyColumns(SqlConnection conn, SqlTransaction tx, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT c.name
            FROM sys.index_columns ic
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            INNER JOIN sys.indexes i ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            WHERE i.is_primary_key = 1 AND ic.object_id = OBJECT_ID(@table)
            ORDER BY ic.key_ordinal";
        cmd.Parameters.Add(new SqlParameter("@table", table));

        var columns = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            columns.Add(reader.GetString(0));
        return columns;
    }

    private static void AddParameters(SqlCommand cmd, Dictionary<string, object> parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
    }

    private static List<Dictionary<string, object?>> ReadRows(SqlDataReader reader)
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
