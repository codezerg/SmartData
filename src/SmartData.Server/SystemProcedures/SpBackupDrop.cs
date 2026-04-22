using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupDrop : SystemStoredProcedure<string>
{
    public string BackupId { get; set; } = "";

    private readonly BackupService _backupService;

    public SpBackupDrop(BackupService backupService) => _backupService = backupService;

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupDrop);
        _backupService.DropBackup(BackupId);
        return $"Backup '{BackupId}' deleted.";
    }
}
