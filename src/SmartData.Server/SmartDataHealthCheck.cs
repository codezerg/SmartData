using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmartData.Server.Providers;

namespace SmartData.Server;

internal class SmartDataHealthCheck : IHealthCheck
{
    private static readonly DateTime StartTime = DateTime.UtcNow;

    private readonly IDatabaseProvider _provider;
    private readonly SessionManager _sessionManager;

    public SmartDataHealthCheck(IDatabaseProvider provider, SessionManager sessionManager)
    {
        _provider = provider;
        _sessionManager = sessionManager;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        bool dbReachable;

        try
        {
            using var conn = _provider.OpenConnection("master");
            dbReachable = true;
        }
        catch
        {
            dbReachable = false;
        }

        data["uptime"] = (DateTime.UtcNow - StartTime).ToString(@"d\.hh\:mm\:ss");
        data["active_sessions"] = _sessionManager.ActiveSessionCount;
        data["db_reachable"] = dbReachable;

        if (!dbReachable)
            return Task.FromResult(HealthCheckResult.Unhealthy("Database unreachable", data: data));

        return Task.FromResult(HealthCheckResult.Healthy("OK", data: data));
    }
}
