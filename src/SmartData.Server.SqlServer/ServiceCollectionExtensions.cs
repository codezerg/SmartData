using Microsoft.Extensions.DependencyInjection;
using SmartData.Server.Providers;

namespace SmartData.Server.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmartDataSqlServer(this IServiceCollection services, Action<SqlServerDatabaseOptions>? configure = null)
    {
        services.Configure<SqlServerDatabaseOptions>(options =>
        {
            options.DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        });
        if (configure != null)
            services.Configure(configure);

        services.AddSingleton<IDatabaseProvider, SqlServerDatabaseProvider>();

        return services;
    }
}
