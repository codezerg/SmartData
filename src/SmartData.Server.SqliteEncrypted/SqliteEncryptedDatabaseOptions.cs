using SmartData.Server.Sqlite;

namespace SmartData.Server.SqliteEncrypted;

public class SqliteEncryptedDatabaseOptions : SqliteDatabaseOptions
{
    /// <summary>
    /// SQLCipher encryption key. Either a passphrase (KDF-derived) or, when
    /// <see cref="UseRawHexKey"/> is true, a 64-char hex string for the raw
    /// 32-byte key. Required — the provider ctor throws if empty.
    /// </summary>
    public string EncryptionKey { get; set; } = string.Empty;

    /// <summary>
    /// When true, <see cref="EncryptionKey"/> is a 64-char hex string and is
    /// applied as <c>PRAGMA key = "x'HEX'"</c> — no KDF, no SQL-string escape
    /// ambiguity. Recommended for production.
    /// </summary>
    public bool UseRawHexKey { get; set; }

    /// <summary>
    /// SQLCipher major-version compatibility. Default 4 (SQLCipher 4.x format).
    /// Set to 3 only when opening a legacy SQLCipher 3 database.
    /// </summary>
    public int CipherCompatibility { get; set; } = 4;
}
