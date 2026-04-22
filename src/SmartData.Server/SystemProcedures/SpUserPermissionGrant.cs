using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpUserPermissionGrant : SystemStoredProcedure<string>
{
    public string UserId { get; set; } = "";
    public string PermissionKey { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.UserGrant);
        db.UseDatabase("master");

        if (string.IsNullOrWhiteSpace(UserId))
            RaiseError("UserId is required.");
        if (string.IsNullOrWhiteSpace(PermissionKey))
            RaiseError("PermissionKey is required.");

        var user = db.GetTable<SysUser>().FirstOrDefault(u => u.Id == UserId);
        if (user == null) RaiseError($"User '{UserId}' not found.");

        var existing = db.GetTable<SysUserPermission>().FirstOrDefault(p => p.UserId == UserId && p.PermissionKey == PermissionKey);
        if (existing != null)
            RaiseError($"Permission '{PermissionKey}' already granted to user '{UserId}'.");

        db.Insert(new SysUserPermission
        {
            UserId = UserId,
            PermissionKey = PermissionKey,
            GrantedAt = DateTime.UtcNow
        });

        return $"Permission '{PermissionKey}' granted to user '{UserId}'.";
    }
}
