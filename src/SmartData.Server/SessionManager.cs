using System.Collections.Concurrent;
using System.Security.Cryptography;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Logging;
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
    public DateTime ExpiresAt { get; set; }
    public int DirtyFlag;   // 0 = clean, 1 = dirty (Interlocked-managed)

    public SessionEntry(UserSession session, DateTime createdAt, DateTime lastActivityAt, DateTime expiresAt)
    {
        Session = session;
        CreatedAt = createdAt;
        LastActivityAt = lastActivityAt;
        ExpiresAt = expiresAt;
    }
}

internal class SessionManager
{
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new();
    private readonly IDatabaseProvider _provider;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<SessionManager> _logger;
    private readonly SessionOptions _options;

    private const string MasterDbName = "master";

    public int ActiveSessionCount => _sessions.Count;

    public SessionManager(
        IDatabaseProvider provider,
        MetricsCollector metrics,
        ILogger<SessionManager> logger,
        IOptions<SessionOptions> options)
    {
        _provider = provider;
        _metrics = metrics;
        _logger = logger;
        _options = options.Value;
    }

    public string? Login(string username, string password)
    {
        using var span = _metrics.StartSpan("auth.login", ("username", username));

        using var db = _provider.OpenConnection(MasterDbName);

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

        user.LastLoginAt = DateTime.UtcNow;
        db.Update(user);

        var permissions = LoadPermissions(db, user);
        var token = GenerateToken();
        var now = DateTime.UtcNow;
        var expiresAt = now + _options.SessionTtl;

        try
        {
            db.Insert(new SysSession
            {
                Token = token,
                UserId = user.Id,
                CreatedAt = now,
                LastActivityAt = now,
                ExpiresAt = expiresAt,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist session for user {UserId}", user.Id);
            _metrics.Counter("auth.login_attempts").Add(1, ("result", "persist_failed"));
            span.SetAttribute("result", "persist_failed");
            return null;
        }

        _sessions[token] = new SessionEntry(
            new UserSession(user.Id, user.Username, user.IsAdmin, permissions),
            now, now, expiresAt);

        _metrics.Counter("auth.login_attempts").Add(1, ("result", "success"));
        _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);
        span.SetAttribute("result", "success");
        return token;
    }

    public UserSession? GetSession(string token)
    {
        if (!_sessions.TryGetValue(token, out var entry))
            return null;

        var now = DateTime.UtcNow;
        if (now >= entry.ExpiresAt)
        {
            if (_sessions.TryRemove(token, out _))
            {
                TryDeleteRow(token);
                _metrics.Counter("auth.sessions_expired").Add(1);
                _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);
            }
            return null;
        }

        if (_options.SlidingExpiration)
        {
            entry.LastActivityAt = now;
            entry.ExpiresAt = now + _options.SessionTtl;
            Interlocked.Exchange(ref entry.DirtyFlag, 1);
        }

        return entry.Session;
    }

    public string? ValidateToken(string token) => GetSession(token)?.UserId;

    public void Logout(string token)
    {
        _sessions.TryRemove(token, out _);
        TryDeleteRow(token);
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

        try
        {
            using var db = _provider.OpenConnection(MasterDbName);
            db.GetTable<SysSession>().Where(s => s.UserId == userId).Delete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete persisted sessions for user {UserId}", userId);
        }

        if (revoked > 0)
            _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);

        return revoked;
    }

    /// <summary>
    /// Removes expired entries from the in-memory cache and the persisted table.
    /// </summary>
    internal int PurgeExpiredSessions()
    {
        var now = DateTime.UtcNow;
        var purged = 0;

        foreach (var kvp in _sessions)
        {
            if (now >= kvp.Value.ExpiresAt && _sessions.TryRemove(kvp.Key, out _))
                purged++;
        }

        try
        {
            using var db = _provider.OpenConnection(MasterDbName);
            db.GetTable<SysSession>().Where(s => s.ExpiresAt <= now).Delete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete expired sessions from database");
        }

        if (purged > 0)
            _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);

        return purged;
    }

    /// <summary>
    /// Loads non-expired sessions from the database into the in-memory cache.
    /// Permissions are recomputed per row from the current SysUser/SysUserPermission tables;
    /// rows whose user no longer exists or is disabled are skipped.
    /// </summary>
    internal int Hydrate()
    {
        var now = DateTime.UtcNow;
        var loaded = 0;

        try
        {
            using var db = _provider.OpenConnection(MasterDbName);
            var rows = db.GetTable<SysSession>().Where(s => s.ExpiresAt > now).ToList();

            foreach (var row in rows)
            {
                var user = db.GetTable<SysUser>().FirstOrDefault(u => u.Id == row.UserId);
                if (user == null || user.IsDisabled)
                    continue;

                var permissions = LoadPermissions(db, user);
                _sessions[row.Token] = new SessionEntry(
                    new UserSession(user.Id, user.Username, user.IsAdmin, permissions),
                    row.CreatedAt, row.LastActivityAt, row.ExpiresAt);
                loaded++;
            }

            _metrics.Gauge("auth.active_sessions").Set(_sessions.Count);
            _logger.LogInformation("Hydrated {Count} sessions from _sys_sessions", loaded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hydrate sessions from database");
        }

        return loaded;
    }

    /// <summary>
    /// Returns a snapshot of (token, lastActivityAt, expiresAt) for entries that have been touched
    /// since the last drain, atomically clearing the dirty flag on each. Used by sp_session_flush.
    /// </summary>
    internal List<(string Token, DateTime LastActivityAt, DateTime ExpiresAt)> DrainDirty()
    {
        var dirty = new List<(string, DateTime, DateTime)>();
        foreach (var kvp in _sessions)
        {
            if (Interlocked.Exchange(ref kvp.Value.DirtyFlag, 0) == 1)
                dirty.Add((kvp.Key, kvp.Value.LastActivityAt, kvp.Value.ExpiresAt));
        }
        return dirty;
    }

    /// <summary>
    /// Flushes coalesced LastActivityAt/ExpiresAt updates to _sys_sessions. Returns rows written.
    /// </summary>
    internal int FlushDirty()
    {
        var dirty = DrainDirty();
        if (dirty.Count == 0)
            return 0;

        var written = 0;
        try
        {
            using var db = _provider.OpenConnection(MasterDbName);
            using var tx = db.BeginTransaction();
            foreach (var (token, lastActivityAt, expiresAt) in dirty)
            {
                written += db.GetTable<SysSession>()
                    .Where(s => s.Token == token)
                    .Set(s => s.LastActivityAt, lastActivityAt)
                    .Set(s => s.ExpiresAt, expiresAt)
                    .Update();
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush {Count} dirty sessions", dirty.Count);
        }

        return written;
    }

    private void TryDeleteRow(string token)
    {
        try
        {
            using var db = _provider.OpenConnection(MasterDbName);
            db.GetTable<SysSession>().Where(s => s.Token == token).Delete();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete session row");
        }
    }

    private List<string> LoadPermissions(DataConnection db, SysUser user)
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
