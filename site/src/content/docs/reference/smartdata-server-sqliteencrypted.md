---
title: SmartData.Server.SqliteEncrypted
description: SQLCipher-encrypted SQLite provider for SmartData.Server.
---

SQLCipher-backed SQLite provider. Extends `SmartData.Server.Sqlite` and swaps the `SQLitePCLRaw` bundle for `bundle_e_sqlcipher`, so `.db` files are AES-256 encrypted at the page level.

## Dependencies

- **SmartData.Server.Sqlite** — provides the schema / DDL / FTS5 / pragma machinery this provider subclasses
- **SQLitePCLRaw.bundle_e_sqlcipher** — SQLCipher-enabled SQLite native binary

## Bundle conflict

`SQLitePCLRaw` bundles are process-exclusive. Referencing both `SmartData.Server.Sqlite` and `SmartData.Server.SqliteEncrypted` in the same app is undefined behavior. Pick one per process.

## Registration

```csharp
builder.Services.AddSmartDataSqliteEncrypted(o =>
{
    o.DataDirectory = "data";
    o.EncryptionKey = Environment.GetEnvironmentVariable("SD_DB_KEY")!;
    o.UseRawHexKey  = true;
});
```

Registers `SqliteEncryptedDatabaseProvider` as both `IDatabaseProvider` and `IEncryptedDatabaseMaintenance`, and auto-registers every `IStoredProcedure` in this assembly (currently `usp_database_rekey`).

## Configuration

```csharp
public class SqliteEncryptedDatabaseOptions : SqliteDatabaseOptions
{
    public string DataDirectory { get; set; }            // from base
    public string EncryptionKey { get; set; }            // required
    public bool   UseRawHexKey  { get; set; }            // default false
    public int    CipherCompatibility { get; set; } = 4; // SQLCipher 4.x
}
```

The provider constructor throws if `EncryptionKey` is empty.

## Key format

- `UseRawHexKey = true` → 64-char hex (raw 32-byte key). Emitted as `PRAGMA key = "x'HEX'"`. No PBKDF2 cost, no escape ambiguity. Recommended in production.
- `UseRawHexKey = false` → passphrase. Single quotes are doubled; emitted as `PRAGMA key = 'pass'`. SQLCipher derives the key via PBKDF2.

## Key rotation — `usp_database_rekey`

RPC body:

```json
{
  "DbName":          "app",
  "CurrentKey":      "<active key>",
  "NewKey":          "<new key>",
  "NewUseRawHexKey": true
}
```

`CurrentKey` must match the active key (error 1003 on mismatch). The procedure runs `PRAGMA rekey` under the current key, updates the in-memory options, and clears the `Microsoft.Data.Sqlite` connection pool.

The procedure does **not** persist the new key. Rotating it into your config / secret store is the caller's responsibility — losing it locks the database forever.

## What it reuses

`AlterColumn` patching, FTS5, throughput pragmas, schema introspection — all inherited verbatim. The only diff vs. `SmartData.Server.Sqlite` is the connection-open path: `PRAGMA key` is the first statement on every `DataConnection` and `SqliteConnection` the provider hands out.
