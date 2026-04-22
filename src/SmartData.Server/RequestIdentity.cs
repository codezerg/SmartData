namespace SmartData.Server;

/// <summary>
/// Request-scoped caller identity for SmartData's own system procedures.
///
/// Holds the authenticated session (if any), the raw token, and a fallback
/// trusted-caller name used by scheduler-driven runs. System procedures read
/// <see cref="UserId"/> for audit columns, <see cref="Session"/> for
/// permission-scoped queries, and call <see cref="Require"/> /
/// <see cref="RequireScoped"/> / <see cref="RequireAny"/> / <see cref="Has"/>
/// to enforce authorization imperatively.
///
/// Populated once per call by <see cref="ProcedureExecutor"/>. Data access
/// goes through the <c>IDatabaseContext</c> passed into
/// <c>SystemStoredProcedure.Execute</c>, not through this type.
///
/// Intentionally <c>internal</c> — user-defined procedures don't see this.
/// User-level identity concerns are out of scope for this library; users wire
/// whatever they need.
///
/// Scope rule: this type is identity-only. Non-identity request state belongs
/// elsewhere (constructor-injected per procedure, or a separate scoped type).
/// </summary>
internal sealed class RequestIdentity
{
    public UserSession? Session { get; private set; }
    public string? Token { get; private set; }
    public string? TrustedUser { get; private set; }

    /// <summary>
    /// Whether the current call is running under framework authority (scheduler,
    /// <see cref="IProcedureService"/>). Every <c>Require*</c> check passes
    /// silently when this is true. Nested <c>ctx.ExecuteAsync</c> calls inherit
    /// the flag so permission gates stay bypassed for the whole chain.
    /// </summary>
    public bool Trusted => TrustedUser != null;

    /// <summary>
    /// Best-effort identity for audit columns. Returns the authenticated user
    /// if there is one, the trusted caller for scheduler-driven runs, or the
    /// <c>"anonymous"</c> sentinel otherwise. Always non-null.
    /// </summary>
    public string UserId => Session?.UserId ?? TrustedUser ?? "anonymous";

    internal void Initialize(UserSession? session, string? token, string? trustedUser)
    {
        Session = session;
        Token = token;
        TrustedUser = trustedUser;
    }

    // -------------------------------------------------------------------------
    // Authorization helpers.
    //
    // Call these at the top of a system procedure's Execute method. Trusted
    // callers (scheduler, IProcedureService) pass unconditionally; unauthenticated
    // callers get UnauthorizedAccessException; authenticated callers are matched
    // against their session permissions using the same rules the old
    // [RequirePermission] attribute enforced (exact, action wildcard, db
    // wildcard, admin bypass).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Require an unscoped permission. Throws
    /// <see cref="UnauthorizedAccessException"/> if the caller is not
    /// authenticated or lacks the permission.
    /// </summary>
    public void Require(string key)
    {
        if (Trusted) return;
        if (Session == null)
            throw new UnauthorizedAccessException("Authentication required.");
        if (!HasInternal(Session, key))
            throw new UnauthorizedAccessException($"Permission '{key}' is required.");
    }

    /// <summary>
    /// Require a permission scoped to a specific database or other namespace.
    /// The composed key is <c>"{scope}:{key}"</c> and is matched using the same
    /// wildcard rules as <see cref="Require"/>.
    /// </summary>
    public void RequireScoped(string key, string scope)
    {
        if (Trusted) return;
        if (Session == null)
            throw new UnauthorizedAccessException("Authentication required.");
        var composed = $"{scope}:{key}";
        if (!HasInternal(Session, composed))
            throw new UnauthorizedAccessException($"Permission '{composed}' is required.");
    }

    /// <summary>
    /// Require any one of the supplied permission keys. Useful for OR-style
    /// gates like "Users:Edit:Self OR Users:Edit:Any".
    /// </summary>
    public void RequireAny(params string[] keys)
    {
        if (Trusted) return;
        if (Session == null)
            throw new UnauthorizedAccessException("Authentication required.");
        foreach (var k in keys)
            if (HasInternal(Session, k)) return;
        throw new UnauthorizedAccessException(
            $"One of [{string.Join(", ", keys)}] is required.");
    }

    /// <summary>
    /// Predicate form — returns <c>true</c> if the caller has the permission
    /// (or is trusted). Does not throw. Use when the procedure needs to branch
    /// on whether a caller has a capability, rather than gating outright.
    /// </summary>
    public bool Has(string key)
    {
        if (Trusted) return true;
        if (Session == null) return false;
        return HasInternal(Session, key);
    }

    /// <summary>Scoped variant of <see cref="Has"/>.</summary>
    public bool HasScoped(string key, string scope)
    {
        if (Trusted) return true;
        if (Session == null) return false;
        return HasInternal(Session, $"{scope}:{key}");
    }

    // Permission-matching rules, verbatim from the old executor gate:
    //  - admin bypass
    //  - exact match
    //  - action wildcard ("a:b:c" matches "a:b:*")
    //  - db wildcard ("a:b:c" matches "*:b:c" and "*:b:*")
    private static bool HasInternal(UserSession session, string key)
    {
        if (session.IsAdmin) return true;
        if (session.Permissions.Contains(key)) return true;

        var lastColon = key.LastIndexOf(':');
        if (lastColon > 0)
        {
            var actionScope = key[..lastColon];
            if (session.Permissions.Contains($"{actionScope}:*")) return true;
        }

        var firstColon = key.IndexOf(':');
        if (firstColon > 0)
        {
            var rest = key[(firstColon + 1)..];
            if (session.Permissions.Contains($"*:{rest}")) return true;

            var restLastColon = rest.LastIndexOf(':');
            if (restLastColon > 0)
            {
                var restScope = rest[..restLastColon];
                if (session.Permissions.Contains($"*:{restScope}:*")) return true;
            }
        }

        return false;
    }
}
