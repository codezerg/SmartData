# SmartData.Server.SqliteEncrypted

SQLCipher-backed SQLite database provider. Extends `SmartData.Server.Sqlite` and swaps the `SQLitePCLRaw` bundle for `bundle_e_sqlcipher`, so `.db` files on disk are AES-256 encrypted at the page level.

## Dependencies

- **SmartData.Server.Sqlite** — provides the schema / DDL / FTS5 / pragma machinery that this provider subclasses
- **SQLitePCLRaw.bundle_e_sqlcipher** — SQLCipher-enabled SQLite native binary

## Bundle conflict

`SQLitePCLRaw` bundles are process-exclusive. Referencing both `SmartData.Server.Sqlite` and `SmartData.Server.SqliteEncrypted` in the same app is undefined behavior — the two bundles register conflicting native symbols. Pick one per process.

## Registration

```csharp
builder.Services.AddSmartDataSqliteEncrypted(o =>
{
    o.DataDirectory = "data";
    o.EncryptionKey = Environment.GetEnvironmentVariable("SD_DB_KEY")!;
    o.UseRawHexKey  = true;   // 64-char hex (recommended)
});
```

Registers `SqliteEncryptedDatabaseProvider` as both `IDatabaseProvider` and `IEncryptedDatabaseMaintenance`. Auto-registers every `IStoredProcedure` in this assembly, which currently means `usp_database_rekey`.

## Configuration

```csharp
public class SqliteEncryptedDatabaseOptions : SqliteDatabaseOptions
{
    public string DataDirectory { get; set; }           // from base
    public string EncryptionKey { get; set; }           // required
    public bool   UseRawHexKey  { get; set; }           // default false
    public int    CipherCompatibility { get; set; } = 4; // SQLCipher 4.x
}
```

The provider constructor throws `InvalidOperationException` if `EncryptionKey` is empty.

## Key format

- `UseRawHexKey = true` → 64-char hex string (32-byte raw key). Emitted as `PRAGMA key = "x'HEX'"`. No PBKDF2 cost, no SQL-string escape ambiguity. Recommended in production.
- `UseRawHexKey = false` → passphrase. Single quotes are doubled; emitted as `PRAGMA key = 'pass'`. SQLCipher derives the key via PBKDF2.

## Key rotation

Call `usp_database_rekey` via RPC with the current and new keys:

```
{ DbName, CurrentKey, NewKey, NewUseRawHexKey }
```

`CurrentKey` must match the active key (message id 1003 on mismatch). The procedure:

1. Opens a pool-bypassed connection, applies the current key, forces a read to verify it.
2. Runs `PRAGMA rekey`. SQLCipher rewrites every page under the new key in one transaction.
3. Updates the in-memory options so subsequent connections use the new key.
4. Clears the Microsoft.Data.Sqlite connection pool so no pooled connection carries the old key.

**The procedure does not persist the new key.** The caller owns rotating it into config / secret store. Losing it locks the database forever.

## What it reuses from SmartData.Server.Sqlite

Everything except the connection-open path. `AlterColumn` patching, FTS5 triggers, throughput pragmas, schema introspection — all inherited verbatim. The only override points are:

- `SqliteDatabaseProvider.OnConnectionOpened` — issues `PRAGMA key` before `ApplyPragmas`.
- `SqliteSchemaProvider.OpenConnection` / `SqliteSchemaOperations.OpenConnection` / `SqliteRawDataProvider.OpenConnection` — each calls `base.OpenConnection` then `ApplyKey(conn)` so raw `SqliteConnection` callers also speak under the key.

## Tests

`tests/SmartData.Server.SqliteEncrypted.Tests/` — covers ctor validation, `FormatKey` escaping, SQLCipher bundle load (`PRAGMA cipher_version`), an AutoRepo round-trip through the keyed `DataConnection` and sub-provider paths, raw-file unreadability under a wrong key, and the `usp_database_rekey` happy/sad paths. Run via `dotnet run --project tests/SmartData.Server.SqliteEncrypted.Tests`.

## Project structure

```
SmartData.Server.SqliteEncrypted/
├── SqliteEncryptedDatabaseProvider.cs      Subclass; OnConnectionOpened → PRAGMA key; Rekey impl
├── SqliteEncryptedDatabaseOptions.cs       EncryptionKey / UseRawHexKey / CipherCompatibility
├── IEncryptedDatabaseMaintenance.cs        Interface for Rekey (resolved from DI)
├── EncryptedSqliteSchemaProvider.cs        Sub-provider override for SqliteConnection keying
├── EncryptedSqliteSchemaOperations.cs      Sub-provider override for SqliteConnection keying
├── EncryptedSqliteRawDataProvider.cs       Sub-provider override for SqliteConnection keying
├── RekeyResult.cs                          Contract for usp_database_rekey
├── Procedures/DatabaseRekey.cs             usp_database_rekey
├── ServiceCollectionExtensions.cs          AddSmartDataSqliteEncrypted
└── README.md
```
