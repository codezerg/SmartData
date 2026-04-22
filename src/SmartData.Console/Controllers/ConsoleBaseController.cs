using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Contracts;
using SmartData.Server;

namespace SmartData.Console.Controllers;

public abstract class ConsoleBaseController : Controller
{
    protected readonly IAuthenticatedProcedureService ProcedureService;

    protected ConsoleBaseController(IAuthenticatedProcedureService procedureService)
    {
        ProcedureService = procedureService;
    }

    protected bool IsHtmx => Request.Headers.ContainsKey("HX-Request");

    protected Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default)
    {
        var token = HttpContext.Items["ConsoleToken"] as string;
        ProcedureService.Authenticate(token);
        return ProcedureService.ExecuteAsync<T>(spName, args, ct);
    }

    protected async Task PopulateLayout(string? db, CancellationToken ct)
    {
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        db ??= databases.FirstOrDefault()?.Name;

        ViewData["Layout"] = new LayoutViewModel
        {
            CurrentPath = Request.Path.Value ?? "/",
            CurrentDb = db,
            Databases = databases,
            Username = HttpContext.Items["ConsoleUser"] as string
        };
    }

    protected IActionResult PageOrPartial(string viewName, object? model = null)
    {
        if (IsHtmx)
            return PartialView(viewName, model);
        return View(viewName, model);
    }
}
