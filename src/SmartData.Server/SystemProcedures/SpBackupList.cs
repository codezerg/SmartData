using SmartData.Contracts;
using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupList : SystemStoredProcedure<List<BackupListItem>>
{
    private readonly BackupService _backupService;

    public SpBackupList(BackupService backupService) => _backupService = backupService;

    public override List<BackupListItem> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupList);
        return _backupService.ListBackups();
    }
}
