namespace SmartData.Server;

/// <summary>
/// In-process procedure caller with framework authority. Bypasses authentication
/// and permission checks — use only from trusted server-side code (schedulers,
/// startup tasks, internal wiring). For user-authenticated calls (RPC, console),
/// use <see cref="IAuthenticatedProcedureService"/>.
/// </summary>
public interface IProcedureService
{
    Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default);
    void QueueExecuteAsync(string spName, object? args = null);
}

/// <summary>
/// Procedure caller that runs under a user's session. Enforces the
/// <c>[AllowAnonymous]</c> gate and any imperative
/// <see cref="RequestIdentity.Require"/> / <c>RequireScoped</c> calls inside
/// system procedures, using the token provided via <see cref="Authenticate"/>.
/// Used by the RPC entry point and the embedded admin console.
/// </summary>
public interface IAuthenticatedProcedureService
{
    void Authenticate(string? token);

    Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default);
    void QueueExecuteAsync(string spName, object? args = null);
}
