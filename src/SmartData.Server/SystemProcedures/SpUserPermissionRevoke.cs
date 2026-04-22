using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpUserPermissionRevoke : SystemStoredProcedure<string>
{
    public string UserId { get; set; } = "";
    public string PermissionKey { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.UserRevoke);
        db.UseDatabase("master");

        if (string.IsNullOrWhiteSpace(UserId))
            RaiseError("UserId is required.");
        if (string.IsNullOrWhiteSpace(PermissionKey))
            RaiseError("PermissionKey is required.");

        var existing = db.GetTable<SysUserPermission>().FirstOrDefault(p => p.UserId == UserId && p.PermissionKey == PermissionKey);
        if (existing == null) RaiseError($"Permission '{PermissionKey}' not found for user '{UserId}'.");

        db.Delete(existing);

        return $"Permission '{PermissionKey}' revoked from user '{UserId}'.";
    }
}
