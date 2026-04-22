using SmartData.Contracts;
using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpStorage : SystemStoredProcedure<object>
{
    public string Database { get; set; } = "";

    private readonly BackupService _backupService;

    public SpStorage(BackupService backupService)
    {
        _backupService = backupService;
    }

    public override object Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.ServerStorage);

        if (!string.IsNullOrEmpty(Database))
            return GetDatabaseView(provider, Database);

        return GetOverview(provider);
    }

    private object GetOverview(IDatabaseProvider provider)
    {
        var databases = provider.ListDatabases()
            .Select(name => new StorageDatabaseItem
            {
                Name = name,
                Size = provider.GetDatabaseInfo(name).Size
            }).ToList();

        var backupItems = _backupService.ListBackups();
        var backups = backupItems.Select(b => new StorageBackupItem
        {
            BackupId = b.BackupId,
            Size = b.Size,
            Databases = b.Databases
        }).ToList();

        var dbSize = databases.Sum(d => d.Size);
        var backupSize = backups.Sum(b => b.Size);

        return new StorageResult
        {
            Databases = databases,
            Backups = backups,
            DbSize = dbSize,
            BackupSize = backupSize,
            TotalSize = dbSize + backupSize
        };
    }

    private static object GetDatabaseView(IDatabaseProvider provider, string dbName)
    {
        var info = provider.GetDatabaseInfo(dbName);

        return new StorageDatabaseResult
        {
            Database = dbName,
            Size = info.Size
        };
    }
}
