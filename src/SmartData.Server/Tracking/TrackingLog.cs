using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartData.Server.Tracking;

/// <summary>
/// Single process-wide logger hook for the tracking subsystem's static code
/// paths (startup resolution, schema provisioning). Wired once during
/// <c>AddSmartData</c>. A fallback <see cref="NullLogger"/> is used if no
/// factory is configured so tests and ad-hoc callers don't need to wire DI.
/// </summary>
internal static class TrackingLog
{
    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger => _logger;

    public static void SetFactory(ILoggerFactory factory)
    {
        _logger = factory.CreateLogger("SmartData.Server.Tracking");
    }
}
