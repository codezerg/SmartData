using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpUserUpdate : SystemStoredProcedure<string>
{
    public string UserId { get; set; } = "";
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool? IsAdmin { get; set; }
    public bool? IsDisabled { get; set; }

    private readonly SessionManager _sessions;

    public SpUserUpdate(SessionManager sessions)
    {
        _sessions = sessions;
    }

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.UserCreate);
        db.UseDatabase("master");

        if (string.IsNullOrWhiteSpace(UserId))
            RaiseError("UserId is required.");

        var user = db.GetTable<SysUser>().FirstOrDefault(u => u.Id == UserId);
        if (user == null) RaiseError($"User '{UserId}' not found.");

        if (!string.IsNullOrWhiteSpace(Username) && Username != user.Username)
        {
            var existing = db.GetTable<SysUser>().FirstOrDefault(u => u.Username == Username && u.Id != UserId);
            if (existing != null)
                RaiseError($"User '{Username}' already exists.");
            user.Username = Username;
        }

        if (!string.IsNullOrWhiteSpace(Password))
        {
            user.PasswordHash = PasswordHasher.HashPassword(Password);
        }

        if (IsAdmin.HasValue)
        {
            user.IsAdmin = IsAdmin.Value;
        }

        if (IsDisabled.HasValue)
        {
            user.IsDisabled = IsDisabled.Value;
        }

        user.ModifiedAt = DateTime.UtcNow;
        db.Update(user);

        if (IsDisabled == true)
            _sessions.RevokeUserSessions(UserId);

        return $"User '{user.Username}' updated.";
    }
}
