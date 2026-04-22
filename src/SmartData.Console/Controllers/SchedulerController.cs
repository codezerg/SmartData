using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Contracts;
using SmartData.Server;

namespace SmartData.Console.Controllers;

public class SchedulerController : ConsoleBaseController
{
    private readonly ConsoleRoutes _routes;

    public SchedulerController(IAuthenticatedProcedureService procedureService, ConsoleRoutes routes) : base(procedureService)
    {
        _routes = routes;
    }

    // ── List ────────────────────────────────────────────────────────────

    [HttpGet("/console/schedulers")]
    public async Task<IActionResult> Index(string? search, string? filter, CancellationToken ct)
    {
        var model = await BuildListAsync(search, filter, null, null, ct);
        await PopulateLayout(null, ct);
        return PageOrPartial("Index", model);
    }

    private async Task<SchedulerListViewModel> BuildListAsync(
        string? search, string? filter, string? success, string? error, CancellationToken ct)
    {
        bool? enabled = filter switch
        {
            "enabled" => true,
            "disabled" => false,
            _ => (bool?)null,
        };

        var result = await ExecuteAsync<ScheduleListResult>("sp_schedule_list",
            new { Search = search, Enabled = enabled }, ct);

        return new SchedulerListViewModel
        {
            Schedules = result.Items,
            Search = search,
            Filter = filter ?? "all",
            SuccessMessage = success,
            ErrorMessage = error,
        };
    }

    // ── Detail ──────────────────────────────────────────────────────────

    [HttpGet("/console/schedulers/{id:int}")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var model = await BuildDetailAsync(id, null, null, ct);
        await PopulateLayout(null, ct);
        return PageOrPartial("Detail", model);
    }

    private async Task<SchedulerDetailViewModel> BuildDetailAsync(int id, string? success, string? error, CancellationToken ct)
    {
        var schedule = await ExecuteAsync<ScheduleGetResult>("sp_schedule_get",
            new { Id = id, RecentRuns = 25 }, ct);

        SchedulePreviewResult? preview = null;
        try
        {
            preview = await ExecuteAsync<SchedulePreviewResult>("sp_schedule_preview",
                new { Id = id, Count = 10 }, ct);
        }
        catch { /* preview is a nice-to-have */ }

        return new SchedulerDetailViewModel
        {
            Schedule = schedule,
            Preview = preview?.NextFireTimes ?? [],
            SuccessMessage = success,
            ErrorMessage = error,
        };
    }

    // ── Actions ─────────────────────────────────────────────────────────

    [HttpPost("/console/schedulers/{id:int}/toggle")]
    public async Task<IActionResult> ToggleEnabled(int id, [FromForm] bool enabled, CancellationToken ct)
    {
        string? success = null, error = null;
        try
        {
            await ExecuteAsync<ScheduleSaveResult>("sp_schedule_update",
                new { Id = id, Enabled = enabled }, ct);
            success = enabled ? "Schedule enabled." : "Schedule disabled.";
        }
        catch (Exception ex) { error = ex.Message; }

        var model = await BuildListAsync(null, null, success, error, ct);
        return PartialView("_ScheduleTable", model);
    }

    [HttpPost("/console/schedulers/{id:int}/start")]
    public async Task<IActionResult> Start(int id, CancellationToken ct)
    {
        string? success = null, error = null;
        try
        {
            var res = await ExecuteAsync<ScheduleStartResult>("sp_schedule_start", new { Id = id }, ct);
            success = res.Message;
        }
        catch (Exception ex) { error = ex.Message; }

        var model = await BuildDetailAsync(id, success, error, ct);
        return PartialView("_DetailBody", model);
    }

    [HttpPost("/console/schedulers/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id, CancellationToken ct)
    {
        string? success = null, error = null;
        try
        {
            await ExecuteAsync<object>("sp_schedule_cancel", new { ScheduleId = id }, ct);
            success = "Cancel requested — in-flight runs will stop on next heartbeat.";
        }
        catch (Exception ex) { error = ex.Message; }

        var model = await BuildDetailAsync(id, success, error, ct);
        return PartialView("_DetailBody", model);
    }

    [HttpPost("/console/schedulers/{id:int}/toggle-detail")]
    public async Task<IActionResult> ToggleEnabledDetail(int id, [FromForm] bool enabled, CancellationToken ct)
    {
        string? success = null, error = null;
        try
        {
            await ExecuteAsync<ScheduleSaveResult>("sp_schedule_update",
                new { Id = id, Enabled = enabled }, ct);
            success = enabled ? "Schedule enabled." : "Schedule disabled.";
        }
        catch (Exception ex) { error = ex.Message; }

        var model = await BuildDetailAsync(id, success, error, ct);
        return PartialView("_DetailBody", model);
    }

    [HttpPost("/console/schedulers/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id,
        [FromForm] int retryAttempts,
        [FromForm] int retryIntervalSeconds,
        [FromForm] int jitterSeconds,
        CancellationToken ct)
    {
        string? success = null, error = null;
        try
        {
            await ExecuteAsync<ScheduleSaveResult>("sp_schedule_update", new
            {
                Id = id,
                RetryAttempts = retryAttempts,
                RetryIntervalSeconds = retryIntervalSeconds,
                JitterSeconds = jitterSeconds,
            }, ct);
            success = "Schedule updated.";
        }
        catch (Exception ex) { error = ex.Message; }

        var model = await BuildDetailAsync(id, success, error, ct);
        return PartialView("_DetailBody", model);
    }

    // ── History ─────────────────────────────────────────────────────────

    [HttpGet("/console/schedulers/history")]
    public async Task<IActionResult> History(string? outcome, string? procedureName, int limit, CancellationToken ct)
    {
        if (limit <= 0) limit = 100;
        var result = await ExecuteAsync<ScheduleHistoryResult>("sp_schedule_history",
            new { Outcome = outcome, ProcedureName = procedureName, Limit = limit }, ct);

        var model = new SchedulerHistoryViewModel
        {
            Runs = result.Items,
            Outcome = outcome,
            ProcedureName = procedureName,
            Limit = limit,
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("History", model);
    }

    // ── Stats ───────────────────────────────────────────────────────────

    [HttpGet("/console/schedulers/stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        var stats = await ExecuteAsync<ScheduleStatsResult>("sp_schedule_stats", new { WindowHours = 24 }, ct);
        var model = new SchedulerStatsViewModel { Stats = stats };
        await PopulateLayout(null, ct);
        return PageOrPartial("Stats", model);
    }
}
