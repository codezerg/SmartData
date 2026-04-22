using Microsoft.Extensions.DependencyInjection;

namespace SmartData.Server.Tracking;

/// <summary>
/// Supplies the <c>ChangedBy</c> value stamped onto every history and ledger
/// row. Replace with a custom implementation if the default resolution
/// (authenticated session → trusted caller → <c>"system"</c>) doesn't fit.
/// </summary>
public interface ITrackingUserProvider
{
    string CurrentUser { get; }
}

/// <summary>
/// Default implementation — reads the request-scoped
/// <see cref="RequestIdentity"/> when one exists; falls back to
/// <c>"system"</c> during startup, seed data, and background work that runs
/// without a request scope.
/// </summary>
internal sealed class DefaultTrackingUserProvider : ITrackingUserProvider
{
    private readonly IServiceProvider _services;
    public DefaultTrackingUserProvider(IServiceProvider services) { _services = services; }

    public string CurrentUser
    {
        get
        {
            var identity = _services.GetService<RequestIdentity>();
            var user = identity?.UserId;
            return string.IsNullOrEmpty(user) || user == "anonymous" ? "system" : user;
        }
    }
}
