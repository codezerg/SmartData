using SmartData.Contracts;
using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupCreate : SystemStoredProcedure<BackupCreateResult>
{
    public string Databases { get; set; } = "";

    private readonly BackupService _backupService;

    public SpBackupCreate(BackupService backupService) => _backupService = backupService;

    public override BackupCreateResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupCreate);
        return _backupService.SubmitCreateJob(Databases, identity.UserId);
    }
}
