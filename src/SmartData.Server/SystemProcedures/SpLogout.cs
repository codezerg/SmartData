using SmartData.Server.Procedures;

namespace SmartData.Server.SystemProcedures;

internal class SpLogout : StoredProcedure<string>
{
    public string Token { get; set; } = "";

    private readonly SessionManager _sessions;

    public SpLogout(SessionManager sessions) => _sessions = sessions;

    public override string Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        _sessions.Logout(Token);
        return "Logged out.";
    }
}
