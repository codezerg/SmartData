using SmartData.Contracts;
using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupStatus : SystemStoredProcedure<BackupJobStatus>
{
    public string JobId { get; set; } = "";

    private readonly BackupService _backupService;

    public SpBackupStatus(BackupService backupService) => _backupService = backupService;

    public override BackupJobStatus Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupList);

        var status = _backupService.GetJobStatus(JobId);
        if (status == null) RaiseError($"Job '{JobId}' not found.");
        return status;
    }
}
