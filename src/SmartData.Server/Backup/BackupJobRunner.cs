using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartData.Server.Backup;

public class BackupJobQueue
{
    private readonly Channel<BackupJob> _channel = Channel.CreateBounded<BackupJob>(100);

    internal void Enqueue(BackupJob job) =>
        _channel.Writer.TryWrite(job);

    internal async Task<BackupJob> DequeueAsync(CancellationToken ct) =>
        await _channel.Reader.ReadAsync(ct);
}

internal class BackupJobRunner : BackgroundService
{
    private readonly BackupJobQueue _queue;
    private readonly BackupService _backupService;
    private readonly ILogger<BackupJobRunner> _logger;

    public BackupJobRunner(BackupJobQueue queue, BackupService backupService, ILogger<BackupJobRunner> logger)
    {
        _queue = queue;
        _backupService = backupService;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _backupService.Initialize();
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = await _queue.DequeueAsync(stoppingToken);
            try
            {
                job.Status = "running";
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cts.Token);

                if (job.Operation == "create")
                    await _backupService.ExecuteCreateJob(job, linked.Token);
                else if (job.Operation == "restore")
                    await _backupService.ExecuteRestoreJob(job, linked.Token);

                job.Status = "completed";
            }
            catch (OperationCanceledException)
            {
                job.Status = "cancelled";
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.Error = ex.Message;
                _logger.LogError(ex, "Backup job {JobId} ({Operation}) failed", job.JobId, job.Operation);
            }
            finally
            {
                _backupService.FinalizeJob(job);
            }
        }
    }
}
