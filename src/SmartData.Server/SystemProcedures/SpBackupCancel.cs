using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupCancel : SystemStoredProcedure<string>
{
    public string JobId { get; set; } = "";

    private readonly BackupService _backupService;

    public SpBackupCancel(BackupService backupService) => _backupService = backupService;

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupCreate);

        var cancelled = _backupService.CancelJob(JobId);
        if (!cancelled)
            RaiseError($"Job '{JobId}' not found or already finished.");
        return $"Job '{JobId}' cancellation requested.";
    }
}
