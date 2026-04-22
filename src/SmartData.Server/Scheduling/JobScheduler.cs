using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Core;
using SmartData.Server.Procedures;

namespace SmartData.Server.Scheduling;

/// <summary>
/// The scheduler's pump. Calls <c>sp_scheduler_tick</c> on a timer so no special
/// scheduling knowledge lives outside that procedure.
/// </summary>
internal sealed class JobScheduler : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SchedulerOptions _options;
    private readonly ILogger<JobScheduler> _logger;

    public JobScheduler(
        IServiceScopeFactory scopes,
        IOptions<SchedulerOptions> options,
        ILogger<JobScheduler> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "JobScheduler starting — poll {Poll}, max concurrent {Max}, instance {Instance}.",
            _options.PollInterval, _options.MaxConcurrentRuns, _options.InstanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var procs = scope.ServiceProvider.GetRequiredService<IProcedureService>();
                await procs.ExecuteAsync<VoidResult>("sp_scheduler_tick");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "sp_scheduler_tick failed — continuing loop.");
            }

            try { await Task.Delay(_options.PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
