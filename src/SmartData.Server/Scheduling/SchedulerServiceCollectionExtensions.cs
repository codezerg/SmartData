using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartData.Server.Scheduling;

namespace SmartData;

/// <summary>
/// Extension methods for registering the scheduling subsystem.
/// Call after <see cref="ServiceCollectionExtensions.AddSmartData"/> and
/// <see cref="ServiceCollectionExtensions.AddStoredProcedures"/> so every
/// registered assembly is visible to the reconciler.
/// </summary>
public static class SchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddSmartDataScheduler(
        this IServiceCollection services,
        Action<SchedulerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Bind SchedulerOptions from SmartDataOptions.Scheduler — matches Metrics/Backup/Session pattern.
        services.AddSingleton(sp =>
        {
            var smartDataOptions = sp.GetRequiredService<IOptions<SmartDataOptions>>();
            return Options.Create(smartDataOptions.Value.Scheduler);
        });

        if (configure != null)
        {
            services.Configure<SmartDataOptions>(o => configure(o.Scheduler));
        }

        services.AddSingleton<ScheduleReconciler>();

        // Reconciler runs first — its StartAsync must return before any other hosted
        // service begins ticking. Registration order matters: AddHostedService
        // appends in DI order, and IHost starts services in that order, awaiting
        // each StartAsync sequentially.
        services.AddHostedService<ScheduleReconciliationHostedService>();

        // JobScheduler polls sp_scheduler_tick.
        services.AddHostedService<JobScheduler>();

        return services;
    }
}
