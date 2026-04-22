using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartData.Server;

internal class BackgroundSpService : BackgroundService
{
    private readonly BackgroundSpQueue _queue;
    private readonly ProcedureExecutor _executor;
    private readonly ILogger<BackgroundSpService> _logger;

    public BackgroundSpService(BackgroundSpQueue queue, ProcedureExecutor executor, ILogger<BackgroundSpService> logger)
    {
        _queue = queue;
        _executor = executor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var work = await _queue.DequeueAsync(stoppingToken);
            try
            {
                await _executor.ExecuteAsync(work.SpName, work.Parameters, stoppingToken, work.Token, work.Trusted, work.TrustedUser);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background SP execution failed: {Sp}", work.SpName);
            }
        }
    }
}
