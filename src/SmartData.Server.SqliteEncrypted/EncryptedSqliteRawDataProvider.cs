using Microsoft.Data.Sqlite;
using SmartData.Server.Sqlite;

namespace SmartData.Server.SqliteEncrypted;

internal sealed class EncryptedSqliteRawDataProvider : SqliteRawDataProvider
{
    private readonly SqliteEncryptedDatabaseProvider _encrypted;

    public EncryptedSqliteRawDataProvider(SqliteEncryptedDatabaseProvider root) : base(root)
    {
        _encrypted = root;
    }

    protected override SqliteConnection OpenConnection(string dbName)
    {
        var conn = base.OpenConnection(dbName);
        _encrypted.ApplyKey(conn);
        return conn;
    }
}
