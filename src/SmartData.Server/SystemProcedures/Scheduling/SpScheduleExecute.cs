using System.Diagnostics;
using LinqToDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Core;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;

namespace SmartData.Server.SystemProcedures.Scheduling;

/// <summary>
/// Runs one pre-claimed <c>SysScheduleRun</c> to completion. Called by the scheduler
/// (queued from <c>sp_scheduler_tick</c> or <c>sp_schedule_start</c>) and callable
/// directly for testing. Bypasses the normal permission gate because scheduled jobs
/// run under framework authority, not user authority.
/// </summary>
[AllowAnonymous]
internal class SpScheduleExecute : SystemAsyncStoredProcedure<VoidResult>
{
    public long RunId { get; set; }

    private readonly IServiceScopeFactory _scopes;
    private readonly ProcedureExecutor _executor;
    private readonly SchedulerOptions _options;
    private readonly ILogger<SpScheduleExecute> _logger;

    public SpScheduleExecute(
        IServiceScopeFactory scopes,
        ProcedureExecutor executor,
        IOptions<SchedulerOptions> options,
        ILogger<SpScheduleExecute> logger)
    {
        _scopes = scopes;
        _executor = executor;
        _options = options.Value;
        _logger = logger;
    }

    public override async Task<VoidResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        db.UseDatabase("master");

        var run = await db.GetTable<SysScheduleRun>().FirstOrDefaultAsync(r => r.Id == RunId, ct);
        if (run == null) RaiseError(2001, $"ScheduleRun {RunId} not found.");

        var schedule = await db.GetTable<SysSchedule>().FirstOrDefaultAsync(s => s.Id == run!.ScheduleId, ct);
        if (schedule == null) RaiseError(2002, $"Schedule {run!.ScheduleId} not found.");
        if (!schedule!.Enabled) RaiseError(2003, $"Schedule '{schedule.Name}' is disabled.");

        run!.StartedOn       = DateTime.Now;
        run.LastHeartbeatAt  = DateTime.Now;
        run.Outcome          = "Running";
        run.InstanceId       = _options.InstanceId;
        await db.UpdateAsync(run, ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = StartHeartbeatAndCancelWatcher(RunId, linked);

        var sw = Stopwatch.StartNew();
        try
        {
            await InvokeTargetAsync(schedule.ProcedureName, schedule.OwnerUser, linked.Token);
            run.Outcome = "Succeeded";
        }
        catch (OperationCanceledException)
        {
            run.Outcome = "Cancelled";
        }
        catch (ProcedureException ex)
        {
            run.Outcome       = "Failed";
            run.Message       = ex.Message;
            run.ErrorId       = ex.MessageId;
            run.ErrorSeverity = (int)ex.Severity;
            if (ShouldRetry(schedule, run.AttemptNumber, ex))
                run.NextAttemptAt = DateTime.Now.AddSeconds(schedule.RetryIntervalSeconds);
        }
        catch (Exception ex)
        {
            run.Outcome = "Failed";
            run.Message = ex.Message;
            run.ErrorId = 0;
            run.ErrorSeverity = (int)ErrorSeverity.Error;
            if (ShouldRetry(schedule, run.AttemptNumber, null))
                run.NextAttemptAt = DateTime.Now.AddSeconds(schedule.RetryIntervalSeconds);
        }
        finally
        {
            sw.Stop();
            run.FinishedOn = DateTime.Now;
            run.DurationMs = sw.ElapsedMilliseconds;
            try { await db.UpdateAsync(run, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Failed to persist final run state for {RunId}", RunId); }

            linked.Cancel();
            try { await heartbeat; } catch { /* watcher caught cancellation */ }
        }

        // Stamp LastRunOn on every completed run so the list/detail views reflect activity.
        // OneTime schedules also auto-disable after firing.
        var dirty = false;
        if (run.StartedOn != default)
        {
            schedule.LastRunOn = run.StartedOn;
            dirty = true;
        }
        if (schedule.FreqType == "OneTime" && schedule.Enabled && run.Outcome != "Cancelled")
        {
            schedule.Enabled = false;
            schedule.NextRunOn = null;
            dirty = true;
        }
        if (dirty)
            await db.UpdateAsync(schedule, ct);

        return VoidResult.Instance;
    }

    private Task InvokeTargetAsync(string procName, string ownerUser, CancellationToken ct) =>
        _executor.ExecuteAsync(
            spName: procName,
            parameters: new Dictionary<string, object>(),
            ct: ct,
            token: null,
            trusted: true,
            trustedUser: ownerUser);

    private static bool ShouldRetry(SysSchedule s, int attempt, ProcedureException? ex)
    {
        if (attempt >= s.RetryAttempts) return false;
        if (ex != null && ex.Severity == ErrorSeverity.Fatal) return false;
        return true;
    }

    private Task StartHeartbeatAndCancelWatcher(long runId, CancellationTokenSource linked)
    {
        var interval = _options.HeartbeatInterval;
        return Task.Run(async () =>
        {
            try
            {
                while (!linked.IsCancellationRequested)
                {
                    await Task.Delay(interval, linked.Token);
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
                    db.UseDatabase("master");
                    await db.GetTable<SysScheduleRun>()
                            .Where(r => r.Id == runId)
                            .Set(r => r.LastHeartbeatAt, DateTime.Now)
                            .UpdateAsync(linked.Token);
                    var cancelled = await db.GetTable<SysScheduleRun>()
                                            .AnyAsync(r => r.Id == runId && r.CancelRequested, linked.Token);
                    if (cancelled) linked.Cancel();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogWarning(ex, "Heartbeat error for run {RunId}", runId); }
        });
    }
}
