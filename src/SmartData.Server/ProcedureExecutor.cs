using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartData.Server.Metrics;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server;

internal class ProcedureExecutor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ProcedureCatalog _catalog;
    private readonly SessionManager _sessionManager;
    private readonly MetricsCollector _metrics;

    public ProcedureExecutor(
        IServiceProvider serviceProvider,
        ProcedureCatalog catalog,
        SessionManager sessionManager,
        MetricsCollector metrics)
    {
        _serviceProvider = serviceProvider;
        _catalog = catalog;
        _sessionManager = sessionManager;
        _metrics = metrics;
    }

    public async Task<object> ExecuteAsync(
        string spName,
        Dictionary<string, object> parameters,
        CancellationToken ct,
        string? token = null,
        bool trusted = false,
        string? trustedUser = null)
    {
        var userSession = !string.IsNullOrEmpty(token) ? _sessionManager.GetSession(token) : null;
        var currentUser = trusted ? (trustedUser ?? "system") : (userSession?.UserId ?? "anonymous");

        // Set procedure context for child components (SqlTrackingInterceptor)
        MetricsContext.CurrentProcedure = spName;

        // The procedure selects its own target database via db.UseDatabase(...)
        // as its first step. The executor seeds a neutral "master" default so
        // GetTable calls fail loudly if a procedure forgets to set its db.
        using var span = _metrics.StartSpan(spName,
            ("procedure", spName), ("user", currentUser));
        var sw = Stopwatch.StartNew();

        using var scope = _serviceProvider.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<RequestIdentity>();
        identity.Initialize(userSession, token, trusted ? trustedUser : null);

        var db = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
        db.UseDatabase("master");

        try
        {
            var spType = _catalog.Resolve(spName);
            var instance = ActivatorUtilities.CreateInstance(scope.ServiceProvider, spType);

            // Trusted callers (e.g. scheduler firing under framework authority) bypass
            // the anonymous-access gate. Per-permission checks are performed
            // imperatively inside each system procedure via RequestIdentity.Require*.
            if (!trusted)
            {
                var allowAnonymous = spType.GetCustomAttribute<AllowAnonymousAttribute>() != null;
                if (!allowAnonymous && userSession == null)
                    throw new UnauthorizedAccessException("Authentication required.");
            }

            foreach (var (key, value) in parameters)
            {
                var prop = spType.GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanWrite)
                {
                    var converted = ConvertParameter(value, prop.PropertyType);
                    prop.SetValue(instance, converted);
                }
            }

            object result;
            if (instance is IAsyncStoredProcedure asyncSp)
                result = await asyncSp.ExecuteAsync(db, ct);
            else if (instance is IStoredProcedure syncSp)
                result = syncSp.Execute(db, ct);
            else
                throw new InvalidOperationException($"Procedure '{spName}' must implement IStoredProcedure or IAsyncStoredProcedure.");

            sw.Stop();
            _metrics.Counter("rpc.requests").Add(1, ("procedure", spName));
            _metrics.Histogram("rpc.duration_ms").Record(sw.Elapsed.TotalMilliseconds, ("procedure", spName));

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            span.SetError(ex);
            _metrics.Counter("rpc.requests").Add(1, ("procedure", spName), ("error", "true"));
            _metrics.Counter("rpc.errors").Add(1, ("procedure", spName), ("error_type", ex.GetType().Name));
            _metrics.Histogram("rpc.duration_ms").Record(sw.Elapsed.TotalMilliseconds, ("procedure", spName));
            _metrics.TrackException(ex, ("procedure", spName), ("user", currentUser));
            throw;
        }
    }

    private static object ConvertParameter(object value, Type targetType)
    {
        if (value == null) return null!;
        if (targetType.IsInstanceOfType(value)) return value;

        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null)
            targetType = underlying;

        if (targetType == typeof(string)) return value.ToString()!;
        if (targetType == typeof(bool) && value is string bs) return bool.Parse(bs);
        if (targetType == typeof(byte[]) && value is byte[] bytes) return bytes;
        return Convert.ChangeType(value, targetType);
    }
}
