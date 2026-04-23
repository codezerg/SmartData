using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SmartData.Server;
using SmartData.Server.Backup;
using SmartData.Server.Metrics;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Tracking;

namespace SmartData;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmartData(this IServiceCollection services, Action<SmartDataOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<SmartDataOptions>(options =>
        {
            options.SchemaMode = SchemaMode.Auto;
        });

        // Set DetailedErrors based on environment (user can override via configure callback)
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<SmartDataOptions>>(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            return new Microsoft.Extensions.Options.ConfigureOptions<SmartDataOptions>(options =>
            {
                options.IncludeExceptionDetails = env.IsDevelopment();
            });
        });

        if (configure != null)
            services.Configure(configure);

        // Metrics — bind MetricsOptions from SmartDataOptions.Metrics
        services.AddSingleton(sp =>
        {
            var smartDataOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmartDataOptions>>();
            return Microsoft.Extensions.Options.Options.Create(smartDataOptions.Value.Metrics);
        });
        services.AddSingleton<MetricsCollector>();
        services.AddHostedService<MetricsFlushService>();

        // Backup — bind BackupOptions from SmartDataOptions.Backup
        services.AddSingleton(sp =>
        {
            var smartDataOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmartDataOptions>>();
            return Microsoft.Extensions.Options.Options.Create(smartDataOptions.Value.Backup);
        });
        services.AddSingleton<BackupJobQueue>();
        services.AddSingleton<BackupService>();
        services.AddHostedService<BackupJobRunner>();

        // Session — bind SessionOptions from SmartDataOptions.Session
        services.AddSingleton(sp =>
        {
            var smartDataOptions = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmartDataOptions>>();
            return Microsoft.Extensions.Options.Options.Create(smartDataOptions.Value.Session);
        });

        services.AddSingleton<DatabaseManager>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<SessionManager>();
        services.AddSingleton<BackgroundSpQueue>();
        services.AddHostedService<BackgroundSpService>();

        // Tracking & ledger — see docs/SmartData.Server.Tracking.md
        services.AddSingleton<TrackingMappingRegistry>();
        services.AddSingleton<TrackedColumnSidecar>();
        services.AddScoped<TrackingWritePath>();
        services.TryAddScoped<ITrackingErrorHandler, DefaultTrackingErrorHandler>();
        services.TryAddScoped<ITrackingUserProvider, DefaultTrackingUserProvider>();
        services.AddSingleton<ProcedureCatalog>();
        services.AddSingleton<ProcedureExecutor>();
        services.AddSingleton<CommandRouter>();
        // Database context — scoped per execution
        services.AddScoped<DatabaseContext>();
        services.AddScoped<IDatabaseContext>(sp => sp.GetRequiredService<DatabaseContext>());
        services.AddScoped<RequestIdentity>();

        services.AddScoped<ProcedureService>();
        services.AddScoped<IProcedureService>(sp => sp.GetRequiredService<ProcedureService>());
        services.AddScoped<AuthenticatedProcedureService>();
        services.AddScoped<IAuthenticatedProcedureService>(sp => sp.GetRequiredService<AuthenticatedProcedureService>());

        // Register system procedures from this assembly
        services.AddStoredProcedures(typeof(ServiceCollectionExtensions).Assembly);

        // Health checks
        services.AddHealthChecks()
            .AddCheck<SmartDataHealthCheck>("smartdata");

        return services;
    }

    /// <summary>
    /// Scans the assembly for all IStoredProcedure implementations and registers them.
    /// Names are derived from class names: PascalCase → sp_snake_case.
    /// </summary>
    public static IServiceCollection AddStoredProcedures(this IServiceCollection services, Assembly assembly)
    {
        services.AddSingleton(new ProcedureAssemblyRegistration(assembly));
        return services;
    }

    /// <summary>
    /// Registers a single stored procedure with a custom name.
    /// </summary>
    public static IServiceCollection AddStoredProcedure<T>(this IServiceCollection services, string name) where T : IStoredProcedure
    {
        services.AddSingleton(new ProcedureRegistration(name, typeof(T)));
        return services;
    }

    /// <summary>
    /// Registers a single async stored procedure with a custom name.
    /// </summary>
    public static IServiceCollection AddAsyncStoredProcedure<T>(this IServiceCollection services, string name) where T : IAsyncStoredProcedure
    {
        services.AddSingleton(new ProcedureRegistration(name, typeof(T)));
        return services;
    }
}

public record ProcedureAssemblyRegistration(Assembly Assembly);
public record ProcedureRegistration(string Name, Type Type);
