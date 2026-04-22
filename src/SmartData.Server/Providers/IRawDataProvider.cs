using System.Data;
using SmartData.Contracts;

namespace SmartData.Server.Providers;

/// <summary>
/// CRUD against dynamic (non-entity) tables.
/// All methods take a database name — the provider resolves connections internally.
/// Filtering uses a WhereClause AST — the provider generates parameterized SQL from it.
/// </summary>
public interface IRawDataProvider
{
    /// <summary>
    /// Opens a streaming IDataReader over all rows in the table.
    /// Caller must dispose the reader (which also closes the connection).
    /// </summary>
    IDataReader OpenReader(string dbName, string table);

    List<Dictionary<string, object?>> Select(string dbName, string table,
        WhereClause? where, string? orderBy, int limit, int offset);

    object Insert(string dbName, string table, Dictionary<string, object?> values);

    int Update(string dbName, string table, Dictionary<string, object?> setValues, WhereClause where);

    int Delete(string dbName, string table, WhereClause where);

    DataImportResult Import(string dbName, string table,
        List<Dictionary<string, object?>> rows, string mode, bool truncate);

    Task<QueryResult> ExecuteRawSql(string dbName, string sql, CancellationToken ct);
}
