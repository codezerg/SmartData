using LinqToDB;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Core;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;

namespace SmartData.Server.SystemProcedures.Scheduling;

/// <summary>
/// The scheduler's sole job. Called on a timer by <see cref="JobScheduler"/>.
/// Four steps in order: due → claim + queue, bounded catch-up, pending retries
/// → claim + queue, orphan sweep.
/// </summary>
[AllowAnonymous]
internal class SpSchedulerTick : SystemAsyncStoredProcedure<VoidResult>
{
    private readonly SchedulerOptions _options;
    private readonly ILogger<SpSchedulerTick> _logger;

    public SpSchedulerTick(IOptions<SchedulerOptions> options, ILogger<SpSchedulerTick> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public override async Task<VoidResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        db.UseDatabase("master");

        var now = DateTime.Now;

        await FireDueSchedulesAsync(db, now, ct);
        await FirePendingRetriesAsync(db, now, ct);
        await SweepOrphansAsync(db, now, ct);

        return VoidResult.Instance;
    }

    private async Task FireDueSchedulesAsync(IDatabaseContext ctx, DateTime now, CancellationToken ct)
    {
        var due = await ctx.GetTable<SysSchedule>()
            .Where(s => s.Enabled
                && s.NextRunOn != null
                && s.NextRunOn <= now
                && (s.EndDate == null || s.EndDate > now))
            .ToListAsync(ct);

        foreach (var s in due)
        {
            if (ct.IsCancellationRequested) break;

            var fireTime = s.NextRunOn!.Value;
            var run = new SysScheduleRun
            {
                ScheduleId = s.Id,
                ScheduledFireTime = fireTime,
                InstanceId = _options.InstanceId,
                StartedOn = now,
                Outcome = "Claimed",
                AttemptNumber = 1,
                RunBy = "scheduler",
                LastHeartbeatAt = now,
            };

            try
            {
                var inserted = await ctx.InsertAsync(run, ct);
                await AdvanceScheduleAsync(ctx, s, fireTime, now, ct);
                ctx.QueueExecuteAsync("sp_schedule_execute", new { RunId = inserted.Id });
            }
            catch (Exception ex)
            {
                // Another instance won the race, or insert failed for another reason — skip.
                _logger.LogDebug(ex, "Claim lost for schedule {Id} fire {Fire}", s.Id, fireTime);
            }
        }
    }

    private async Task AdvanceScheduleAsync(IDatabaseContext ctx, SysSchedule s, DateTime fireTime, DateTime now, CancellationToken ct)
    {
        s.LastRunOn = fireTime;
        s.NextRunOn = SlotComputer.NextFire(s, fireTime);

        // Bounded catch-up — queue up to MaxCatchUp missed slots.
        var caughtUp = 0;
        while (s.NextRunOn.HasValue && s.NextRunOn.Value <= now && caughtUp < _options.MaxCatchUp)
        {
            var missed = s.NextRunOn.Value;
            var missedRun = new SysScheduleRun
            {
                ScheduleId = s.Id,
                ScheduledFireTime = missed,
                InstanceId = _options.InstanceId,
                StartedOn = now,
                Outcome = "Claimed",
                AttemptNumber = 1,
                RunBy = "scheduler",
                LastHeartbeatAt = now,
            };
            try
            {
                var inserted = await ctx.InsertAsync(missedRun, ct);
                ctx.QueueExecuteAsync("sp_schedule_execute", new { RunId = inserted.Id });
            }
            catch { /* dup — another instance claimed */ }
            s.LastRunOn = missed;
            s.NextRunOn = SlotComputer.NextFire(s, missed);
            caughtUp++;
        }

        // Drop any further missed fires — roll NextRunOn forward to the next future slot.
        var drops = 0;
        while (s.NextRunOn.HasValue && s.NextRunOn.Value <= now && drops < 10_000)
        {
            s.NextRunOn = SlotComputer.NextFire(s, s.NextRunOn.Value);
            drops++;
        }
        if (drops > 0)
            _logger.LogInformation("Schedule {Proc}.{Name} dropped {Drops} missed fire(s) during catch-up.", s.ProcedureName, s.Name, drops);

        await ctx.UpdateAsync(s, ct);
    }

    private async Task FirePendingRetriesAsync(IDatabaseContext ctx, DateTime now, CancellationToken ct)
    {
        var pending = await ctx.GetTable<SysScheduleRun>()
            .Where(r => r.Outcome == "Failed" && r.NextAttemptAt != null && r.NextAttemptAt <= now)
            .ToListAsync(ct);

        foreach (var failed in pending)
        {
            if (ct.IsCancellationRequested) break;

            var retry = new SysScheduleRun
            {
                ScheduleId = failed.ScheduleId,
                ScheduledFireTime = failed.NextAttemptAt!.Value,
                InstanceId = _options.InstanceId,
                StartedOn = now,
                Outcome = "Claimed",
                AttemptNumber = failed.AttemptNumber + 1,
                RunBy = "scheduler",
                LastHeartbeatAt = now,
            };

            try
            {
                var inserted = await ctx.InsertAsync(retry, ct);
                failed.NextAttemptAt = null;
                await ctx.UpdateAsync(failed, ct);
                ctx.QueueExecuteAsync("sp_schedule_execute", new { RunId = inserted.Id });
            }
            catch
            {
                // Another instance claimed the retry — null out to avoid re-scan.
                failed.NextAttemptAt = null;
                try { await ctx.UpdateAsync(failed, ct); } catch { /* ignore */ }
            }
        }
    }

    private async Task SweepOrphansAsync(IDatabaseContext ctx, DateTime now, CancellationToken ct)
    {
        var cutoff = now - _options.OrphanTimeout;
        var updated = await ctx.GetTable<SysScheduleRun>()
            .Where(r => (r.Outcome == "Claimed" || r.Outcome == "Running")
                && ((r.LastHeartbeatAt == null && r.StartedOn < cutoff)
                    || (r.LastHeartbeatAt != null && r.LastHeartbeatAt < cutoff)))
            .Set(r => r.Outcome, "Failed")
            .Set(r => r.Message, "Orphaned — instance stopped heartbeating")
            .Set(r => r.FinishedOn, (DateTime?)now)
            .UpdateAsync(ct);

        if (updated > 0)
            _logger.LogWarning("Swept {Count} orphaned schedule run(s).", updated);
    }
}
