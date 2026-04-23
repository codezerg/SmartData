namespace SmartData.Server.SqliteEncrypted;

/// <summary>
/// Maintenance operations specific to an encrypted SQLite provider. Resolved
/// from DI when <see cref="ServiceCollectionExtensions.AddSmartDataSqliteEncrypted"/>
/// has been called.
/// </summary>
public interface IEncryptedDatabaseMaintenance
{
    /// <summary>
    /// Rewrites every page of <paramref name="dbName"/> under a new encryption
    /// key. The caller is responsible for persisting the new key — this method
    /// does not write to config or secret stores. Losing the new key locks the
    /// database forever.
    /// </summary>
    void Rekey(string dbName, string newKey, bool newUseRawHexKey);
}
