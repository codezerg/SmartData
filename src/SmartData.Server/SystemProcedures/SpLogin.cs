using SmartData.Contracts;
using SmartData.Server.Procedures;

namespace SmartData.Server.SystemProcedures;

[AllowAnonymous]
internal class SpLogin : StoredProcedure<LoginResult>
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    private readonly SessionManager _sessions;

    public SpLogin(SessionManager sessions)
    {
        _sessions = sessions;
    }

    public override LoginResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var token = _sessions.Login(Username, Password);
        if (token == null)
            throw new UnauthorizedAccessException("Invalid username or password.");

        return new LoginResult { Token = token };
    }
}
