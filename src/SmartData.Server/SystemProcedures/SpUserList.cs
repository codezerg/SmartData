using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpUserList : SystemStoredProcedure<List<UserListItem>>
{
    public override List<UserListItem> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.UserList);
        db.UseDatabase("master");

        var users = db.GetTable<SysUser>()
            .OrderBy(u => u.Username)
            .Select(u => new UserListItem
            {
                Id = u.Id,
                Username = u.Username,
                IsAdmin = u.IsAdmin,
                IsDisabled = u.IsDisabled,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            })
            .ToList();

        return users;
    }
}
