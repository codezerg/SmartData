using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpUserPermissionList : SystemStoredProcedure<UserPermissionListResult>
{
    public string UserId { get; set; } = "";

    public override UserPermissionListResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.UserList);
        db.UseDatabase("master");

        if (string.IsNullOrWhiteSpace(UserId))
            RaiseError("UserId is required.");

        var user = db.GetTable<SysUser>().FirstOrDefault(u => u.Id == UserId);
        if (user == null) RaiseError($"User '{UserId}' not found.");

        List<string> keys;

        if (user.IsAdmin)
        {
            keys = Permissions.System.Select(p => p.Key)
                .Concat(Permissions.Scoped.Select(p => $"*:{p.Key}"))
                .ToList();
        }
        else
        {
            keys = db.GetTable<SysUserPermission>()
                .Where(p => p.UserId == UserId)
                .Select(p => p.PermissionKey)
                .ToList();
        }

        return new UserPermissionListResult { UserId = UserId, Permissions = keys };
    }
}
