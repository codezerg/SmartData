using SmartData.Contracts;
using SmartData.Server.Backup;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpBackupDownload : SystemAsyncStoredProcedure<BackupDownloadResult>
{
    public string BackupId { get; set; } = "";
    public long Offset { get; set; }
    public int ChunkSize { get; set; } = 1024 * 1024;

    private readonly BackupService _backupService;

    public SpBackupDownload(BackupService backupService) => _backupService = backupService;

    public override async Task<BackupDownloadResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.BackupDownload);
        return await _backupService.DownloadChunk(BackupId, Offset, ChunkSize);
    }
}
