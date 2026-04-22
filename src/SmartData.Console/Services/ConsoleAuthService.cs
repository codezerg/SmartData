using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartData.Contracts;
using SmartData.Server;

namespace SmartData.Console.Services;

public class ConsoleAuthService
{
    private readonly ConcurrentDictionary<string, ConsoleSession> _sessions = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionOptions _options;

    public ConsoleAuthService(IServiceScopeFactory scopeFactory, IOptions<SessionOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    public async Task<string?> LoginAsync(string username, string password)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var procedures = scope.ServiceProvider.GetRequiredService<IAuthenticatedProcedureService>();

            var result = await procedures.ExecuteAsync<LoginResult>("sp_login",
                new { Username = username, Password = password },
                CancellationToken.None);

            var serverToken = result.Token;
            if (serverToken == null) return null;

            procedures.Authenticate(serverToken);
            var session = await procedures.ExecuteAsync<SessionResult>("sp_session",
                null, CancellationToken.None);

            var consoleToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            _sessions[consoleToken] = new ConsoleSession(
                session.Username,
                serverToken,
                DateTime.UtcNow);

            return consoleToken;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public ConsoleSession? GetSession(string? token)
    {
        if (token == null) return null;
        if (!_sessions.TryGetValue(token, out var session)) return null;

        // Expire console sessions using the same TTL as server sessions
        if (DateTime.UtcNow >= session.CreatedAt + _options.SessionTtl)
        {
            _sessions.TryRemove(token, out _);
            return null;
        }

        return session;
    }

    public string? GetUsername(string? token) => GetSession(token)?.Username;

    public async Task LogoutAsync(string? token)
    {
        if (token != null && _sessions.TryRemove(token, out var session))
        {
            using var scope = _scopeFactory.CreateScope();
            var procedures = scope.ServiceProvider.GetRequiredService<IAuthenticatedProcedureService>();
            procedures.Authenticate(session.ServerToken);
            await procedures.ExecuteAsync<string>("sp_logout",
                new { Token = session.ServerToken },
                CancellationToken.None);
        }
    }
}

public record ConsoleSession(string Username, string ServerToken, DateTime CreatedAt);
