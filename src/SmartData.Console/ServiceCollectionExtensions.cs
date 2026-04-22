using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartData.Console;
using SmartData.Console.Controllers;
using SmartData.Console.Services;

namespace SmartData;

public static class ConsoleMvcServiceCollectionExtensions
{
    public static IServiceCollection AddSmartDataConsole(this IServiceCollection services, Action<ConsoleOptions>? configure = null)
    {
        if (configure != null)
            services.Configure(configure);
        else
            services.Configure<ConsoleOptions>(_ => { });

        services.AddHttpContextAccessor();
        services.AddSingleton<ConsoleAuthService>();
        services.AddSingleton<ConsoleRoutes>();

        services.AddControllersWithViews()
            .AddApplicationPart(typeof(DatabaseController).Assembly);

        services.AddOptions<MvcOptions>()
            .Configure<IOptions<ConsoleOptions>>((mvcOptions, consoleOptions) =>
            {
                mvcOptions.Conventions.Add(new ConsoleRoutePrefixConvention(consoleOptions.Value.RoutePrefix));
            });

        return services;
    }
}
