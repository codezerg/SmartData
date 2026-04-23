using SmartData.Core;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;
using SmartData.Server.Scheduling.Attributes;

namespace SmartData.Server.SystemProcedures.Session;

/// <summary>
/// Periodic flush of coalesced sliding-expiration touches into <c>_sys_sessions</c>.
/// Reads remain in-memory; only structural mutations and this batched flush hit the database.
/// </summary>
[Job("Session Flush", Category = "Session",
     Description = "Flushes coalesced LastActivityAt updates into _sys_sessions.")]
[Every(60, Unit.Seconds)]
[AllowAnonymous]
internal class SpSessionFlush : SystemStoredProcedure<VoidResult>
{
    private readonly SessionManager _sessions;

    public SpSessionFlush(SessionManager sessions) { _sessions = sessions; }

    public override VoidResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        _sessions.FlushDirty();
        return VoidResult.Instance;
    }
}
