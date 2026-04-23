using Microsoft.Extensions.Options;
using SmartData.Server.Procedures;

namespace SmartData.Server.SqliteEncrypted.Procedures;

/// <summary>
/// Auto-registers as <c>usp_database_rekey</c>. Rekeys an encrypted SQLite
/// database. <see cref="CurrentKey"/> is required and must match the active
/// key — an authenticated RPC caller cannot silently lock the database by
/// calling this with only a new key.
/// </summary>
public class DatabaseRekey : StoredProcedure<RekeyResult>
{
    public string DbName { get; set; } = string.Empty;
    public string CurrentKey { get; set; } = string.Empty;
    public string NewKey { get; set; } = string.Empty;
    public bool NewUseRawHexKey { get; set; }

    private readonly IEncryptedDatabaseMaintenance _maintenance;
    private readonly IOptions<SqliteEncryptedDatabaseOptions> _options;

    public DatabaseRekey(
        IEncryptedDatabaseMaintenance maintenance,
        IOptions<SqliteEncryptedDatabaseOptions> options)
    {
        _maintenance = maintenance;
        _options = options;
    }

    public override RekeyResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(DbName)) RaiseError(1001, "DbName is required.");
        if (string.IsNullOrEmpty(NewKey)) RaiseError(1002, "NewKey is required.");
        if (CurrentKey != _options.Value.EncryptionKey)
            RaiseError(1003, "CurrentKey does not match — rekey aborted.");

        _maintenance.Rekey(DbName, NewKey, NewUseRawHexKey);
        return new RekeyResult { Success = true, DbName = DbName };
    }
}
