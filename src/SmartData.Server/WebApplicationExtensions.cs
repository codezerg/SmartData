using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Core.Api;
using SmartData.Core.BinarySerialization;
using SmartData.Server;
using SmartData.Server.Procedures;
using SmartData.Server.Tracking;

namespace SmartData;

public static class WebApplicationExtensions
{
    public static WebApplication UseSmartData(this WebApplication app)
    {
        // Wire the tracking subsystem's static logger hook — used by sticky-
        // resolution warnings emitted from TrackingSchemaManager<T>, which is
        // a static generic class and therefore can't take ILogger via DI.
        var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
        TrackingLog.SetFactory(loggerFactory);
        SchemaLog.SetFactory(loggerFactory);
        SchemaLog.RelaxOrphanNotNull =
            app.Services.GetRequiredService<IOptions<SmartDataOptions>>().Value.RelaxOrphanNotNull;

        app.Services.GetRequiredService<DatabaseManager>().EnsureMasterDatabase();
        app.Services.GetRequiredService<SettingsService>().LoadFromDatabase();
        app.Services.GetRequiredService<SessionManager>().Hydrate();

        // Pre-register the fluent mapping for every [Tracked] / [Ledger] type
        // in all scanned assemblies, before any request can hit TrackingWritePath.
        // Avoids the first-write race that lets LinqToDB cache a fallback
        // descriptor for HistoryEntity<T> and corrupt all subsequent writes.
        var trackingRegistry = app.Services.GetRequiredService<TrackingMappingRegistry>();
        var assemblies = app.Services.GetServices<ProcedureAssemblyRegistration>()
            .Select(r => r.Assembly).Distinct();
        var trackedTypes = assemblies.SelectMany(a =>
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
        });
        trackingRegistry.RegisterAll(trackedTypes!);

        app.MapPost("/rpc", async (HttpContext ctx, CommandRouter router) =>
        {
            using var ms = new MemoryStream();
            await ctx.Request.Body.CopyToAsync(ms);
            var requestData = ms.ToArray();

            var request = BinarySerializer.Deserialize<CommandRequest>(requestData)
                ?? new CommandRequest();

            var response = await router.RouteAsync(request);

            var responseData = BinarySerializer.Serialize(response);
            ctx.Response.ContentType = "application/x-binaryrpc";
            await ctx.Response.Body.WriteAsync(responseData);
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var result = new
                {
                    status = report.Status.ToString(),
                    duration = report.TotalDuration.ToString(),
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = e.Value.Description,
                        data = e.Value.Data
                    })
                };
                await context.Response.WriteAsJsonAsync(result);
            }
        });

        return app;
    }
}
