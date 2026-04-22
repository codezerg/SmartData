using SmartData.Contracts;
using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupRestore : SystemStoredProcedure<BackupRestoreResult>
{
    public string BackupId { get; set; } = "";
    public bool Force { get; set; }

    private readonly BackupService _backupService;

    public SpBackupRestore(BackupService backupService) => _backupService = backupService;

    public override BackupRestoreResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupRestore);
        return _backupService.SubmitRestoreJob(BackupId, Force, identity.UserId);
    }
}
