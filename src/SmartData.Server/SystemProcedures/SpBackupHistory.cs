using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupHistory : SystemStoredProcedure<List<BackupHistoryEntry>>
{
    private readonly BackupService _backupService;

    public SpBackupHistory(BackupService backupService) => _backupService = backupService;

    public override List<BackupHistoryEntry> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupHistory);
        return _backupService.GetHistory();
    }
}
