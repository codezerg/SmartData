using LinqToDB.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Server.Providers;

namespace SmartData.Server.SqlServer;

public class SqlServerDatabaseProvider : IDatabaseProvider
{
    private readonly string _baseConnectionString;
    private readonly string _dataDirectory;

    public SqlServerDatabaseProvider(IOptions<SqlServerDatabaseOptions> options, ILogger<SqlServerDatabaseProvider> logger)
    {
        _baseConnectionString = options.Value.ConnectionString;
        _dataDirectory = options.Value.DataDirectory;
        Directory.CreateDirectory(_dataDirectory);

        Schema = new SqlServerSchemaProvider(this);
        SchemaOperations = new SqlServerSchemaOperations(this, logger);
        RawData = new SqlServerRawDataProvider(this);
    }

    public ISchemaProvider Schema { get; }
    public ISchemaOperations SchemaOperations { get; }
    public IRawDataProvider RawData { get; }
    public string DataDirectory => _dataDirectory;

    public DataConnection OpenConnection(string dbName)
    {
        var connStr = GetConnectionString(dbName);
        var conn = new DataConnection("SqlServer", connStr);
        ApplySessionDefaults(conn);
        return conn;
    }

    /// <summary>
    /// Ledger integrity depends on <c>XACT_ABORT OFF</c> — see
    /// <c>docs/SmartData.Server.Tracking.md</c> § Dependencies and
    /// § Pre-implementation Spikes #2. Under <c>XACT_ABORT ON</c> the server
    /// destroys the transaction on a <c>UNIQUE(PrevHash)</c> violation
    /// (verified: <c>XACT_STATE()=0</c>, preceding writes gone), which would
    /// erase the source mutation alongside the failed ledger insert. We
    /// enforce <c>OFF</c> on every connection regardless of caller-supplied
    /// session options.
    /// </summary>
    private static void ApplySessionDefaults(DataConnection conn)
    {
        conn.Execute("SET XACT_ABORT OFF");
    }

    public void EnsureDatabase(string dbName)
    {
        var actualName = MapDbName(dbName);
        using var conn = OpenMasterConnection();
        ExecuteSql(conn, $@"
            IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = @name)
                CREATE DATABASE [{actualName}]",
            new SqlParameter("@name", actualName));
    }

    public void DropDatabase(string dbName)
    {
        var actualName = MapDbName(dbName);
        using var conn = OpenMasterConnection();

        // Force close existing connections then drop
        ExecuteSql(conn, $@"
            IF EXISTS (SELECT 1 FROM sys.databases WHERE name = @name)
            BEGIN
                ALTER DATABASE [{actualName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{actualName}];
            END",
            new SqlParameter("@name", actualName));
    }

    public IEnumerable<string> ListDatabases()
    {
        using var conn = OpenMasterConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var databases = new List<string>();
        while (reader.Read())
            databases.Add(reader.GetString(0));

        // Remap _smartdata back to "master" for the framework
        return databases
            .Select(n => string.Equals(n, "_smartdata", StringComparison.OrdinalIgnoreCase) ? "master" : n)
            .OrderBy(n => n)
            .ToList();
    }

    public bool DatabaseExists(string dbName)
    {
        var actualName = MapDbName(dbName);
        try
        {
            using var conn = OpenMasterConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
            cmd.Parameters.Add(new SqlParameter("@name", actualName));
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch
        {
            return false;
        }
    }

    public DatabaseInfo GetDatabaseInfo(string dbName)
    {
        using var conn = OpenDbConnection(dbName);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                @logicalName AS Name,
                CAST(SUM(CAST(size AS BIGINT)) * 8192 AS BIGINT) AS SizeBytes,
                (SELECT create_date FROM sys.databases WHERE name = DB_NAME()) AS CreatedAt,
                GETUTCDATE() AS ModifiedAt
            FROM sys.database_files";
        cmd.Parameters.Add(new SqlParameter("@logicalName", dbName));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException($"Database '{dbName}' not found.");

        return new DatabaseInfo(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetDateTime(2),
            reader.GetDateTime(3));
    }

    public string BuildFullTextSearchSql(string table, string[] columns, int limit)
    {
        var colList = string.Join(", ", columns.Select(c => $"[{c}]"));
        return $"SELECT TOP({limit}) t.* FROM [{table}] t " +
               $"WHERE CONTAINS(({colList}), @searchTerm)";
    }

    // --- Internal helpers used by SqlServer sub-providers ---

    /// <summary>
    /// Maps a SmartData logical database name to an actual SQL Server database name.
    /// "master" is reserved in SQL Server, so we remap it to "_smartdata".
    /// </summary>
    internal static string MapDbName(string dbName) =>
        string.Equals(dbName, "master", StringComparison.OrdinalIgnoreCase) ? "_smartdata" : dbName;

    internal string GetConnectionString(string dbName)
    {
        var builder = new SqlConnectionStringBuilder(_baseConnectionString)
        {
            InitialCatalog = MapDbName(dbName)
        };
        return builder.ConnectionString;
    }

    internal SqlConnection OpenDbConnection(string dbName)
    {
        var conn = new SqlConnection(GetConnectionString(dbName));
        conn.Open();
        ApplyRawSessionDefaults(conn);
        return conn;
    }

    internal SqlConnection OpenMasterConnection()
    {
        var builder = new SqlConnectionStringBuilder(_baseConnectionString)
        {
            InitialCatalog = "master"
        };
        var conn = new SqlConnection(builder.ConnectionString);
        conn.Open();
        ApplyRawSessionDefaults(conn);
        return conn;
    }

    private static void ApplyRawSessionDefaults(SqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SET XACT_ABORT OFF";
        cmd.ExecuteNonQuery();
    }

    private static void ExecuteSql(SqlConnection conn, string sql, params SqlParameter[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddRange(parameters);
        cmd.ExecuteNonQuery();
    }
}
