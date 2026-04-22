using SmartData.Core;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpUserCreate : SystemStoredProcedure<string>
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.UserCreate);
        db.UseDatabase("master");

        if (string.IsNullOrWhiteSpace(Username))
            RaiseError("Username is required.");
        if (string.IsNullOrWhiteSpace(Password))
            RaiseError("Password is required.");

        var existing = db.GetTable<SysUser>().FirstOrDefault(u => u.Username == Username);
        if (existing != null)
            RaiseError($"User '{Username}' already exists.");

        var hash = PasswordHasher.HashPassword(Password);

        db.Insert(new SysUser
        {
            Id = IdGenerator.NewId(),
            Username = Username,
            PasswordHash = hash,

            CreatedAt = DateTime.UtcNow
        });

        return $"User '{Username}' created.";
    }
}
