using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using SmartData.Server.Providers;

namespace SmartData.Server.Procedures;

public class StoredProcedureCommon
{
    [DoesNotReturn]
    public static void RaiseError(string message) => throw new ProcedureException(message);
    [DoesNotReturn]
    public static void RaiseError(int messageId, string message, ErrorSeverity severity = ErrorSeverity.Error) =>
        throw new ProcedureException(messageId, message, severity);
}

public abstract class StoredProcedure<TResult> : StoredProcedureCommon, IStoredProcedure
{
    public abstract TResult Execute(IDatabaseContext ctx, CancellationToken ct);

    object IStoredProcedure.Execute(IDatabaseContext ctx, CancellationToken ct) => Execute(ctx, ct)!;
}

public abstract class AsyncStoredProcedure<TResult> : StoredProcedureCommon, IAsyncStoredProcedure
{
    public abstract Task<TResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct);

    async Task<object> IAsyncStoredProcedure.ExecuteAsync(IDatabaseContext ctx, CancellationToken ct) => (await ExecuteAsync(ctx, ct))!;
}

/// <summary>
/// Base class for SmartData's own system procedures. Receives the request
/// identity alongside the context. Does NOT set the active database — each
/// procedure must call <c>db.UseDatabase("master")</c> or
/// <c>db.UseDatabase(Database)</c> (from its own parameter) as its first step.
/// Permission checks are imperative via <see cref="RequestIdentity.Require"/>
/// and friends; the declarative <c>[RequirePermission]</c> attribute no longer
/// exists.
/// </summary>
internal abstract class SystemStoredProcedure<TResult> : StoredProcedureCommon, IStoredProcedure
{
    public abstract TResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct);

    object IStoredProcedure.Execute(IDatabaseContext db, CancellationToken ct)
    {
        var ctx = db.Services.GetRequiredService<RequestIdentity>();
        var provider = db.Services.GetRequiredService<IDatabaseProvider>();
        return Execute(ctx, db, provider, ct)!;
    }
}

/// <summary>Async variant of <see cref="SystemStoredProcedure{TResult}"/>.</summary>
internal abstract class SystemAsyncStoredProcedure<TResult> : StoredProcedureCommon, IAsyncStoredProcedure
{
    public abstract Task<TResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct);

    async Task<object> IAsyncStoredProcedure.ExecuteAsync(IDatabaseContext db, CancellationToken ct)
    {
        var ctx = db.Services.GetRequiredService<RequestIdentity>();
        var provider = db.Services.GetRequiredService<IDatabaseProvider>();
        return (await ExecuteAsync(ctx, db, provider, ct))!;
    }
}
