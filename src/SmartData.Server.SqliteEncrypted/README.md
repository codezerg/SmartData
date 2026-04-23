# SmartData.Server.SqliteEncrypted

SQLCipher-backed SQLite provider for SmartData. AES-256, page-level encryption of the database file.

## Bundle conflict warning

This package pulls `SQLitePCLRaw.bundle_e_sqlcipher`, which is **mutually exclusive** with the default `bundle_e_sqlite3` used by `SmartData.Server.Sqlite`. Referencing both in one process is undefined behavior — pick one.

## Registration

```csharp
services.AddSmartDataSqliteEncrypted(o =>
{
    o.DataDirectory = "/var/smartdata";
    o.EncryptionKey = "64-char-hex-string-...";
    o.UseRawHexKey  = true;   // recommended in production
});
```

## Key management

- Prefer `UseRawHexKey = true` with a 64-char hex string (raw 32-byte key). Skips the PBKDF2 cost and avoids SQL-string escape ambiguity.
- Passphrase mode works (`UseRawHexKey = false`) — single quotes are doubled, but keys containing binary / NUL are not safely representable.
- Load the key from an environment variable or secret manager — never commit it to config files.

## Key rotation

Invoke `usp_database_rekey` (RPC) with the current and new keys:

```jsonc
{
  "DbName": "app",
  "CurrentKey": "<active key>",
  "NewKey":     "<new key>",
  "NewUseRawHexKey": true
}
```

The procedure requires `CurrentKey` to match the active key, rewrites every page under the new key, and updates the in-memory options so subsequent connections succeed. **Persisting the new key in your config / secret store is your responsibility** — losing it locks the database forever.

## What it reuses

Everything from `SmartData.Server.Sqlite` — AutoRepo DDL, AlterColumn patching, FTS5, all five throughput pragmas. Only the connection-open path differs: `PRAGMA key` is issued as the very first statement on every connection, before any other SQL.
