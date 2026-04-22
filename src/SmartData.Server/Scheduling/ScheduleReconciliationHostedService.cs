using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmartData.Server.Scheduling;

/// <summary>
/// Tiny <see cref="IHostedService"/> whose sole job is to run <see cref="ScheduleReconciler.ReconcileAsync"/>
/// once during startup — before any other background service begins work.
/// Registered via <c>AddHostedService</c> before <see cref="JobScheduler"/>; because
/// <see cref="IHostedService.StartAsync"/> is awaited sequentially, the JobScheduler
/// cannot tick until this has returned.
/// </summary>
internal sealed class ScheduleReconciliationHostedService : IHostedService
{
    private readonly ScheduleReconciler _reconciler;
    private readonly ILogger<ScheduleReconciliationHostedService> _logger;

    public ScheduleReconciliationHostedService(
        ScheduleReconciler reconciler,
        ILogger<ScheduleReconciliationHostedService> logger)
    {
        _reconciler = reconciler;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _reconciler.ReconcileAsync(cancellationToken);
            _logger.LogInformation("Schedule reconciliation complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Schedule reconciliation failed — scheduler starting with stale schedules.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
