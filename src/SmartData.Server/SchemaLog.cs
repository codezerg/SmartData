using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SmartData.Server;

/// <summary>
/// Process-wide logger + defaults hook for <see cref="SchemaManager{T}"/>.
/// <see cref="SchemaManager{T}"/> is a static generic class and can't take
/// ILogger / options via DI, so values are set once during <c>UseSmartData</c>.
/// A <see cref="NullLogger"/> fallback keeps tests and ad-hoc callers safe.
/// </summary>
internal static class SchemaLog
{
    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger => _logger;

    /// <summary>
    /// Mirrors <see cref="SmartDataOptions.RelaxOrphanNotNull"/>. Defaults to
    /// true so ad-hoc callers (tests, tools) get the safe behavior.
    /// </summary>
    public static bool RelaxOrphanNotNull { get; set; } = true;

    public static void SetFactory(ILoggerFactory factory)
    {
        _logger = factory.CreateLogger("SmartData.Server.Schema");
    }
}
