using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpSession : SystemStoredProcedure<SessionResult>
{
    public override SessionResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        var session = identity.Session;
        if (session == null)
            throw new UnauthorizedAccessException("No active session.");

        return new SessionResult
        {
            UserId = session.UserId,
            Username = session.Username,
            IsAdmin = session.IsAdmin,
            Permissions = session.Permissions
        };
    }
}
