using LinqToDB.Data;
using Microsoft.Extensions.Options;
using SmartData.Server.Providers;

namespace SmartData.Server.Sqlite;

public class SqliteDatabaseProvider : IDatabaseProvider
{
    private readonly string _dataDirectory;

    public SqliteDatabaseProvider(IOptions<SqliteDatabaseOptions> options)
    {
        _dataDirectory = options.Value.DataDirectory;
        Directory.CreateDirectory(_dataDirectory);

        Schema = new SqliteSchemaProvider(this);
        SchemaOperations = new SqliteSchemaOperations(this);
        RawData = new SqliteRawDataProvider(this);
    }

    public ISchemaProvider Schema { get; }
    public ISchemaOperations SchemaOperations { get; }
    public IRawDataProvider RawData { get; }
    public string DataDirectory => _dataDirectory;

    public DataConnection OpenConnection(string dbName)
    {
        var connStr = $"Data Source={GetDbFilePath(dbName)}";
        var db = new DataConnection("SQLite", connStr);
        // WAL: concurrent readers alongside a writer, no readers-block-writers.
        db.Execute("PRAGMA journal_mode = WAL;");
        // NORMAL: one fsync per checkpoint instead of per commit. Safe under WAL —
        // the only window of loss is the last few commits on a power-cut, never
        // corruption. FULL is paranoid for WAL.
        db.Execute("PRAGMA synchronous = NORMAL;");
        // Make the busy handler explicit: wait up to 5s for a writer lock before
        // throwing SQLITE_BUSY. Default varies by provider; pinning makes the
        // contention behavior predictable.
        db.Execute("PRAGMA busy_timeout = 5000;");
        // ~20MB page cache (negative = KB). Keeps hot indexes + History rows in
        // RAM across transactions.
        db.Execute("PRAGMA cache_size = -20000;");
        // Temp B-trees and sort spills stay in memory instead of a temp file.
        db.Execute("PRAGMA temp_store = MEMORY;");
        return db;
    }

    public void EnsureDatabase(string dbName)
    {
        Directory.CreateDirectory(_dataDirectory);
    }

    public void DropDatabase(string dbName)
    {
        var dbPath = GetDbFilePath(dbName);
        if (!File.Exists(dbPath))
            throw new InvalidOperationException($"Database '{dbName}' does not exist.");

        File.Delete(dbPath);
    }

    public IEnumerable<string> ListDatabases()
    {
        return Directory.GetFiles(_dataDirectory, "*.db")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name)!;
    }

    public bool DatabaseExists(string dbName) =>
        File.Exists(GetDbFilePath(dbName));

    public DatabaseInfo GetDatabaseInfo(string dbName)
    {
        var file = new FileInfo(GetDbFilePath(dbName));
        if (!file.Exists)
            throw new InvalidOperationException($"Database '{dbName}' not found.");

        return new DatabaseInfo(dbName, file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc);
    }

    public string BuildFullTextSearchSql(string table, string[] columns, int limit)
    {
        return $"SELECT t.* FROM [{table}] t " +
               $"INNER JOIN [{table}_fts] fts ON t.[Id] = fts.rowid " +
               $"WHERE [{table}_fts] MATCH @searchTerm " +
               $"ORDER BY rank LIMIT {limit}";
    }

    // --- Internal helpers used by Sqlite sub-providers ---

    internal string GetConnectionString(string dbName) =>
        $"Data Source={GetDbFilePath(dbName)}";

    internal string GetDbFilePath(string dbName) =>
        Path.Combine(_dataDirectory, $"{dbName}.db");

}
