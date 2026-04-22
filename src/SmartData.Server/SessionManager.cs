using System.Collections.Concurrent;
using System.Security.Cryptography;
using LinqToDB;
using Microsoft.Extensions.Options;
using SmartData.Server.Entities;
using SmartData.Server.Metrics;
using SmartData.Server.Providers;

namespace SmartData.Server;

public record UserSession(string UserId, string Username, bool IsAdmin, List<string> Permissions);

internal class SessionEntry
{
    public UserSession Session { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastActivityAt { get; set; }

    public SessionEntry(UserSession session)
    {
        Session = session;
        CreatedAt = DateTime.UtcNow;
        LastActivityAt = DateTime.UtcNow;
    }
}

internal class SessionManager
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly IDatabaseProvider _provider;
    private readonly MetricsCollector _metrics;
    private readonly SessionOptions _options;

    public int ActiveSessionCount => _sessions.Count;

    public SessionManager(IDatabaseProvider provider, MetricsCollector metrics, IOptions<SessionOptions> options)
    {
        _provider = provider;
        _metrics = metrics;
        _options = options.Value;
    }

    public string? Login(string username, string password)
    {
        using var span = _metrics.StartSpan("auth.login", ("username", username));

        using var db = _provider.OpenConnection("master");

        var user = db.GetTable<SysUser>().FirstOrDefault(u => u.Username == username);
        if (user == null || !PasswordHasher.VerifyPassword(password, user.PasswordHash))
        {
            _metrics.Counter("auth.login_attempts").Add(1, ("result", "failure"));
            span.SetAttribute("result", "failure");
            return null;
        }

        if (user.IsDisabled)
        {
            _metrics.Counter("auth.login_attempts").Add(1, ("result", "disabled"));
            span.SetAttribute("result", "disabled");
            return null;
        }

        // Update last login timestamp
        user.LastLoginAt = DateTime.UtcNow;
        db.Update(user);

        var permissions = LoadPermissions(db, user);
        var token = GenerateToken();
        _sessions[token] = new SessionEntry(new UserSession(user.Id, user.Username, user.IsAdmin, permissions));

        _metrics.Counter("auth.login_attempts").Add(1, ("result", "success"));
        _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);
        span.SetAttribute("result", "success");
        return token;
    }

    public UserSession? GetSession(string token)
    {
        if (!_sessions.TryGetValue(token, out var entry))
            return null;

        // Check expiration
        var now = DateTime.UtcNow;
        var expiresAt = _options.SlidingExpiration
            ? entry.LastActivityAt + _options.SessionTtl
            : entry.CreatedAt + _options.SessionTtl;

        if (now >= expiresAt)
        {
            _sessions.TryRemove(token, out _);
            _metrics.Counter("auth.sessions_expired").Add(1);
            _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);
            return null;
        }

        // Slide expiration window on activity
        if (_options.SlidingExpiration)
            entry.LastActivityAt = now;

        return entry.Session;
    }

    public string? ValidateToken(string token)
    {
        return GetSession(token)?.UserId;
    }

    public void Logout(string token)
    {
        _sessions.TryRemove(token, out _);
        _metrics.Counter("auth.logouts").Add(1);
        _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);
    }

    public int RevokeUserSessions(string userId)
    {
        var revoked = 0;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.Session.UserId == userId && _sessions.TryRemove(kvp.Key, out _))
                revoked++;
        }

        if (revoked > 0)
            _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);

        return revoked;
    }

    internal int PurgeExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var purged = 0;

        foreach (var kvp in _sessions)
        {
            var entry = kvp.Value;
            var expiresAt = _options.SlidingExpiration
                ? entry.LastActivityAt + _options.SessionTtl
                : entry.CreatedAt + _options.SessionTtl;

            if (now >= expiresAt && _sessions.TryRemove(kvp.Key, out _))
                purged++;
        }

        if (purged > 0)
            _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);

        return purged;
    }

    private List<string> LoadPermissions(LinqToDB.Data.DataConnection db, SysUser user)
    {
        if (user.IsAdmin)
        {
            return Permissions.System.Select(p => p.Key)
                .Concat(Permissions.Scoped.Select(p => $"*:{p.Key}"))
                .ToList();
        }

        return db.GetTable<SysUserPermission>()
            .Where(p => p.UserId == user.Id)
            .Select(p => p.PermissionKey)
            .ToList();
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

}
