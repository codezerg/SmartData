using Microsoft.Extensions.DependencyInjection;
using SmartData.Server.Providers;

namespace SmartData.Server.Sqlite;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmartDataSqlite(this IServiceCollection services, Action<SqliteDatabaseOptions>? configure = null)
    {
        services.Configure<SqliteDatabaseOptions>(options =>
        {
            options.DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        });
        if (configure != null)
            services.Configure(configure);

        services.AddSingleton<IDatabaseProvider, SqliteDatabaseProvider>();

        return services;
    }
}
