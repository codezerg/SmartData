using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpUserDelete : SystemStoredProcedure<string>
{
    public string UserId { get; set; } = "";

    private readonly SessionManager _sessions;

    public SpUserDelete(SessionManager sessions)
    {
        _sessions = sessions;
    }

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.UserDelete);
        db.UseDatabase("master");

        if (string.IsNullOrWhiteSpace(UserId))
            RaiseError("UserId is required.");

        var user = db.GetTable<SysUser>().FirstOrDefault(u => u.Id == UserId);
        if (user == null) RaiseError($"User '{UserId}' not found.");

        if (user.IsAdmin)
            RaiseError("Cannot delete an admin user.");

        db.Delete<SysUserPermission>(p => p.UserId == UserId);
        db.Delete(user);

        _sessions.RevokeUserSessions(UserId);

        return $"User '{user.Username}' deleted.";
    }
}
