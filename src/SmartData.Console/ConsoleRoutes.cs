using Microsoft.Extensions.Options;

namespace SmartData.Console;

/// <summary>
/// Provides the normalized console route prefix for use in controllers and views.
/// </summary>
public class ConsoleRoutes
{
    public string Prefix { get; }

    public ConsoleRoutes(IOptions<ConsoleOptions> options)
    {
        Prefix = options.Value.RoutePrefix.Trim('/');
    }

    /// <summary>Builds an absolute path under the console prefix, e.g. "/console/login".</summary>
    public string Path(string relative = "") =>
        string.IsNullOrEmpty(relative) ? $"/{Prefix}" : $"/{Prefix}/{relative.TrimStart('/')}";
}
