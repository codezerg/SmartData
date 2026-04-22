using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Console;

namespace SmartData;

public static class ConsoleMvcWebApplicationExtensions
{
    public static WebApplication UseSmartDataConsole(this WebApplication app)
    {
        var options = app.Services.GetService<IOptions<ConsoleOptions>>()?.Value ?? new ConsoleOptions();

        if (!app.Environment.IsDevelopment() && !options.AllowInProduction)
        {
            app.Logger.LogInformation("SmartData Console disabled in non-development environment.");
            return app;
        }

        if (!app.Environment.IsDevelopment())
        {
            app.Logger.LogWarning(
                "SmartData Console is enabled in a non-development environment. " +
                "This exposes full database access.");
        }

        app.UseMiddleware<ConsoleAuthMiddleware>();

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()?.Addresses;

            var baseUrl = addresses?.FirstOrDefault() ?? "http://localhost";
            app.Logger.LogInformation("SmartData Console available at {ConsoleUrl}", $"{baseUrl}/{options.RoutePrefix.Trim('/')}");
        });

        return app;
    }
}
