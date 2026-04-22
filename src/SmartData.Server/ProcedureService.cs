using SmartData.Core.BinarySerialization;

namespace SmartData.Server;

/// <summary>
/// Trusted in-process caller. Every invocation runs with framework authority —
/// no session lookup, no permission gate. Audit rows attribute to <c>"system"</c>.
/// </summary>
internal class ProcedureService : IProcedureService
{
    private const string TrustedUser = "system";

    private readonly ProcedureExecutor _executor;
    private readonly BackgroundSpQueue _backgroundQueue;

    public ProcedureService(ProcedureExecutor executor, BackgroundSpQueue backgroundQueue)
    {
        _executor = executor;
        _backgroundQueue = backgroundQueue;
    }

    public async Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default)
    {
        var parameters = ArgsHelper.ToDict(args);
        var result = await _executor.ExecuteAsync(spName, parameters, ct, token: null, trusted: true, trustedUser: TrustedUser);
        return ArgsHelper.Cast<T>(result);
    }

    public void QueueExecuteAsync(string spName, object? args = null)
    {
        var parameters = ArgsHelper.ToDict(args);
        _backgroundQueue.Enqueue(new BackgroundSpWork(spName, parameters, Token: null, Trusted: true, TrustedUser: TrustedUser));
    }
}

/// <summary>
/// User-authenticated in-process caller. Runs procedures through the full auth
/// gate using the token supplied via <see cref="Authenticate"/>. Used by the RPC
/// entry point and the embedded admin console.
/// </summary>
internal class AuthenticatedProcedureService : IAuthenticatedProcedureService
{
    private readonly ProcedureExecutor _executor;
    private readonly BackgroundSpQueue _backgroundQueue;
    private string? _token;

    public AuthenticatedProcedureService(ProcedureExecutor executor, BackgroundSpQueue backgroundQueue)
    {
        _executor = executor;
        _backgroundQueue = backgroundQueue;
    }

    public void Authenticate(string? token) => _token = token;

    public async Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default)
    {
        var parameters = ArgsHelper.ToDict(args);
        var result = await _executor.ExecuteAsync(spName, parameters, ct, _token);
        return ArgsHelper.Cast<T>(result);
    }

    public void QueueExecuteAsync(string spName, object? args = null)
    {
        var parameters = ArgsHelper.ToDict(args);
        _backgroundQueue.Enqueue(new BackgroundSpWork(spName, parameters, _token));
    }
}

internal static class ArgsHelper
{
    public static Dictionary<string, object> ToDict(object? args)
    {
        if (args == null) return new();
        if (args is Dictionary<string, object> dict) return dict;

        var bytes = BinarySerializer.Serialize(args);
        return BinarySerializer.Deserialize<Dictionary<string, object>>(bytes) ?? new();
    }

    public static T Cast<T>(object result)
    {
        if (result is T typed) return typed;
        var bytes = BinarySerializer.Serialize(result);
        return BinarySerializer.Deserialize<T>(bytes)!;
    }
}
