using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Core.Api;
using SmartData.Core.BinarySerialization;
using SmartData.Server.Metrics;
using SmartData.Server.Procedures;

namespace SmartData.Server;

internal class CommandRouter
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SessionManager _sessionManager;
    private readonly MetricsCollector _metrics;
    private readonly SmartDataOptions _options;
    private readonly ILogger<CommandRouter> _logger;

    public CommandRouter(IServiceScopeFactory scopes, SessionManager sessionManager, MetricsCollector metrics,
        IOptions<SmartDataOptions> options, ILogger<CommandRouter> logger)
    {
        _scopes = scopes;
        _sessionManager = sessionManager;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CommandResponse> RouteAsync(CommandRequest request)
    {
        var spName = request.Command;
        var hasToken = !string.IsNullOrEmpty(request.Token);
        var authenticated = hasToken && _sessionManager.GetSession(request.Token!) != null;

        _metrics.Gauge("rpc.active_requests").Increment();

        try
        {
            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (request.Args is { Length: > 0 })
            {
                var args = BinarySerializer.Deserialize<Dictionary<string, object>>(request.Args);
                if (args != null)
                {
                    foreach (var (key, value) in args)
                        parameters[key] = value;
                }
            }

            using var scope = _scopes.CreateScope();
            var procs = scope.ServiceProvider.GetRequiredService<IAuthenticatedProcedureService>();
            procs.Authenticate(request.Token);
            var result = await procs.ExecuteAsync<object>(spName, parameters, CancellationToken.None);
            var response = CommandResponse.Ok(result);
            response.Authenticated = hasToken ? authenticated : null;
            return response;
        }
        catch (ProcedureException ex)
        {
            var response = CommandResponse.Fail(ex.Message);
            if (ex.MessageId != 0) response.ErrorId = ex.MessageId;
            response.ErrorSeverity = (int)ex.Severity;
            response.Authenticated = hasToken ? authenticated : null;
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Procedure {Procedure} failed", spName);
            var message = _options.IncludeExceptionDetails ? ex.Message : "An internal error occurred.";
            var response = CommandResponse.Fail(message);
            response.Authenticated = hasToken ? authenticated : null;
            return response;
        }
        finally
        {
            _metrics.Gauge("rpc.active_requests").Decrement();
        }
    }
}
