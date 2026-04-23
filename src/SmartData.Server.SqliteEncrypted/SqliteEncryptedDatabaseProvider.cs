using System.Collections.Concurrent;
using LinqToDB.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SmartData.Server.Providers;
using SmartData.Server.Sqlite;

namespace SmartData.Server.SqliteEncrypted;

public class SqliteEncryptedDatabaseProvider : SqliteDatabaseProvider, IEncryptedDatabaseMaintenance
{
    private readonly SqliteEncryptedDatabaseOptions _options;
    private readonly ConcurrentDictionary<string, object> _dbLocks = new(StringComparer.OrdinalIgnoreCase);

    public SqliteEncryptedDatabaseProvider(IOptions<SqliteEncryptedDatabaseOptions> options)
        : base(Options.Create<SqliteDatabaseOptions>(options.Value))
    {
        _options = options.Value;
        if (string.IsNullOrEmpty(_options.EncryptionKey))
            throw new InvalidOperationException(
                $"{nameof(SqliteEncryptedDatabaseOptions)}.{nameof(SqliteEncryptedDatabaseOptions.EncryptionKey)} must be set.");
        if (_options.UseRawHexKey)
            ValidateHexKey(_options.EncryptionKey);
    }

    protected override void OnConnectionOpened(DataConnection db, string dbName)
    {
        // SQLCipher requires PRAGMA key before any other statement; this hook
        // runs before ApplyPragmas in the base template method.
        db.Execute($"PRAGMA key = {FormatKey(_options.EncryptionKey, _options.UseRawHexKey)};");
        if (_options.CipherCompatibility != 4)
            db.Execute($"PRAGMA cipher_compatibility = {_options.CipherCompatibility};");
    }

    protected override ISchemaProvider   CreateSchemaProvider()   => new EncryptedSqliteSchemaProvider(this);
    protected override ISchemaOperations CreateSchemaOperations() => new EncryptedSqliteSchemaOperations(this);
    protected override IRawDataProvider  CreateRawDataProvider()  => new EncryptedSqliteRawDataProvider(this);

    /// <summary>
    /// Applies <c>PRAGMA key</c> (and optional <c>cipher_compatibility</c>) to a
    /// freshly-opened <see cref="SqliteConnection"/>. Called from the keyed
    /// sub-provider overrides immediately after <c>conn.Open()</c>.
    /// </summary>
    internal void ApplyKey(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA key = {FormatKey(_options.EncryptionKey, _options.UseRawHexKey)};";
        cmd.ExecuteNonQuery();
        if (_options.CipherCompatibility != 4)
        {
            cmd.CommandText = $"PRAGMA cipher_compatibility = {_options.CipherCompatibility};";
            cmd.ExecuteNonQuery();
        }
    }

    public void Rekey(string dbName, string newKey, bool newUseRawHexKey)
    {
        if (string.IsNullOrEmpty(newKey))
            throw new ArgumentException("newKey is required.", nameof(newKey));
        if (newUseRawHexKey) ValidateHexKey(newKey);
        if (!DatabaseExists(dbName))
            throw new InvalidOperationException($"Database '{dbName}' does not exist.");

        var gate = _dbLocks.GetOrAdd(dbName, _ => new object());
        lock (gate)
        {
            // Pooling=False: this one-shot must not return to the pool under the
            // old key after rekey — we want it closed and forgotten.
            var connStr = $"Data Source={GetDbFilePath(dbName)};Pooling=False";
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = $"PRAGMA key = {FormatKey(_options.EncryptionKey, _options.UseRawHexKey)};";
                    cmd.ExecuteNonQuery();

                    // PRAGMA key itself never errors — SQLCipher defers validation
                    // to the first real read. Force one here so a wrong current
                    // key fails loud before we attempt rekey.
                    cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
                    try { cmd.ExecuteScalar(); }
                    catch (SqliteException ex)
                    {
                        throw new InvalidOperationException(
                            "Current encryption key is incorrect — rekey aborted.", ex);
                    }

                    cmd.CommandText = $"PRAGMA rekey = {FormatKey(newKey, newUseRawHexKey)};";
                    cmd.ExecuteNonQuery();
                }
            }

            // Mutate the cached options so every subsequent OpenConnection uses
            // the new key. Safe because we hold the per-db lock and the options
            // singleton is owned by this provider.
            _options.EncryptionKey = newKey;
            _options.UseRawHexKey = newUseRawHexKey;

            // Evict any pooled connections carrying the old key. ClearPool
            // requires an instance; a throwaway suffices since it keys by
            // connection string.
            using var evict = new SqliteConnection($"Data Source={GetDbFilePath(dbName)}");
            SqliteConnection.ClearPool(evict);
        }
    }

    /// <summary>
    /// Formats an encryption key for <c>PRAGMA key</c> / <c>PRAGMA rekey</c>.
    /// Raw-hex mode emits <c>"x'HEX'"</c>; passphrase mode doubles single
    /// quotes and emits <c>'escaped'</c>.
    /// </summary>
    public static string FormatKey(string key, bool useRawHex)
    {
        if (useRawHex)
        {
            ValidateHexKey(key);
            return $"\"x'{key}'\"";
        }
        return $"'{key.Replace("'", "''")}'";
    }

    private static void ValidateHexKey(string key)
    {
        if (key.Length != 64)
            throw new ArgumentException(
                "UseRawHexKey requires a 64-char hex string (32 bytes).", nameof(key));
        for (var i = 0; i < key.Length; i++)
        {
            var c = key[i];
            var isHex = (c >= '0' && c <= '9')
                     || (c >= 'a' && c <= 'f')
                     || (c >= 'A' && c <= 'F');
            if (!isHex)
                throw new ArgumentException(
                    "UseRawHexKey requires a 64-char hex string (32 bytes).", nameof(key));
        }
    }
}
