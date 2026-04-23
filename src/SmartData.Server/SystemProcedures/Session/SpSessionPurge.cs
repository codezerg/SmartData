using SmartData.Core;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;
using SmartData.Server.Scheduling.Attributes;

namespace SmartData.Server.SystemProcedures.Session;

/// <summary>
/// Periodic purge of expired sessions from both the in-memory cache and <c>_sys_sessions</c>.
/// Replaces the former <c>SessionCleanupService</c>.
/// </summary>
[Job("Session Purge", Category = "Session",
     Description = "Deletes expired sessions from memory and _sys_sessions.")]
[Every(60, Unit.Seconds)]
[AllowAnonymous]
internal class SpSessionPurge : SystemStoredProcedure<VoidResult>
{
    private readonly SessionManager _sessions;

    public SpSessionPurge(SessionManager sessions) { _sessions = sessions; }

    public override VoidResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        _sessions.PurgeExpiredSessions();
        return VoidResult.Instance;
    }
}
