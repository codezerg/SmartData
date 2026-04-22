using SmartData.Contracts;
using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupUpload : SystemAsyncStoredProcedure<BackupUploadResult>
{
    public string BackupId { get; set; } = "";
    public byte[] Data { get; set; } = [];
    public long Offset { get; set; }
    public long TotalSize { get; set; }

    private readonly BackupService _backupService;

    public SpBackupUpload(BackupService backupService) => _backupService = backupService;

    public override async Task<BackupUploadResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupUpload);
        return await _backupService.UploadChunk(BackupId, Data, Offset, TotalSize);
    }
}
