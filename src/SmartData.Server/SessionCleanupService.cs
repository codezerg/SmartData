using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Server.Metrics;

namespace SmartData.Server;

internal sealed class SessionCleanupService : BackgroundService
{
    private readonly SessionManager _sessionManager;
    private readonly SessionOptions _options;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SessionCleanupService> _logger;

    public SessionCleanupService(
        SessionManager sessionManager,
        IOptions<SessionOptions> options,
        MetricsCollector metrics,
        ILogger<SessionCleanupService> logger)
    {
        _sessionManager = sessionManager;
        _options = options.Value;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.CleanupIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(interval, stoppingToken);

            var purged = _sessionManager.PurgeExpiredSessions();
            if (purged > 0)
                _logger.LogInformation("Purged {Count} expired sessions", purged);
        }
    }
}
