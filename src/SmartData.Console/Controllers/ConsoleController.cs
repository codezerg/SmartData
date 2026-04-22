using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Contracts;
using SmartData.Server;

namespace SmartData.Console.Controllers;

public class ConsoleController : ConsoleBaseController
{
    public ConsoleController(IAuthenticatedProcedureService procedureService) : base(procedureService) { }

    [HttpGet("/console")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        var storage = await ExecuteAsync<StorageResult>("sp_storage", ct: ct);

        // Metrics and exceptions may fail (disabled, permissions) — don't break the dashboard
        MetricsResult metrics;
        try { metrics = await ExecuteAsync<MetricsResult>("sp_metrics", new { Source = "live", PageSize = 200 }, ct); }
        catch { metrics = new MetricsResult(); }

        ExceptionsResult exceptions;
        try { exceptions = await ExecuteAsync<ExceptionsResult>("sp_exceptions", new { PageSize = 5 }, ct); }
        catch { exceptions = new ExceptionsResult(); }

        var model = new DashboardViewModel
        {
            Databases = databases,
            Metrics = metrics,
            RecentExceptions = exceptions,
            Storage = storage
        };

        await PopulateLayout(null, ct);
        return PageOrPartial("Index", model);
    }
}
