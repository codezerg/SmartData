using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures.Scheduling;

internal class SpScheduleStats : SystemAsyncStoredProcedure<ScheduleStatsResult>
{
    public int WindowHours { get; set; } = 24;

    public override async Task<ScheduleStatsResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerList);
        db.UseDatabase("master");

        var since = DateTime.Now.AddHours(-Math.Max(1, WindowHours));

        var total = await db.GetTable<SysSchedule>().CountAsync(ct);
        var enabled = await db.GetTable<SysSchedule>().CountAsync(s => s.Enabled, ct);
        var running = await db.GetTable<SysScheduleRun>().CountAsync(r => r.Outcome == "Claimed" || r.Outcome == "Running", ct);
        var retries = await db.GetTable<SysScheduleRun>().CountAsync(r => r.Outcome == "Failed" && r.NextAttemptAt != null, ct);
        var succ = await db.GetTable<SysScheduleRun>().CountAsync(r => r.Outcome == "Succeeded" && r.StartedOn >= since, ct);
        var fail = await db.GetTable<SysScheduleRun>().CountAsync(r => r.Outcome == "Failed" && r.StartedOn >= since, ct);
        var canc = await db.GetTable<SysScheduleRun>().CountAsync(r => r.Outcome == "Cancelled" && r.StartedOn >= since, ct);

        var perProc = await (from r in db.GetTable<SysScheduleRun>()
                             where r.StartedOn >= since && r.Outcome == "Succeeded"
                             join s in db.GetTable<SysSchedule>() on r.ScheduleId equals s.Id
                             group r by s.ProcedureName into g
                             select new ScheduleProcedureStat
                             {
                                 ProcedureName = g.Key,
                                 AvgDurationMs = g.Average(x => x.DurationMs),
                                 Runs = g.Count(),
                             }).ToListAsync(ct);

        return new ScheduleStatsResult
        {
            SchedulesTotal = total,
            SchedulesEnabled = enabled,
            CurrentlyRunning = running,
            PendingRetries = retries,
            Last24hSucceeded = succ,
            Last24hFailed = fail,
            Last24hCancelled = canc,
            PerProcedure = perProc,
        };
    }
}
