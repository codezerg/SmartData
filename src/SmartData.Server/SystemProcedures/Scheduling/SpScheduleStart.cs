using LinqToDB;
using Microsoft.Extensions.Options;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;

namespace SmartData.Server.SystemProcedures.Scheduling;

internal class SpScheduleStart : SystemAsyncStoredProcedure<ScheduleStartResult>
{
    public int Id { get; set; }

    private readonly SchedulerOptions _options;

    public SpScheduleStart(IOptions<SchedulerOptions> options) { _options = options.Value; }

    public override async Task<ScheduleStartResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerRun);
        db.UseDatabase("master");

        var schedule = await db.GetTable<SysSchedule>().FirstOrDefaultAsync(s => s.Id == Id, ct);
        if (schedule == null) RaiseError(2101, $"Schedule {Id} not found.");

        var now = DateTime.Now;
        var run = new SysScheduleRun
        {
            ScheduleId = schedule!.Id,
            ScheduledFireTime = now,
            InstanceId = _options.InstanceId,
            StartedOn = now,
            Outcome = "Claimed",
            AttemptNumber = 1,
            RunBy = identity.UserId,
            LastHeartbeatAt = now,
        };
        var inserted = await db.InsertAsync(run, ct);

        db.QueueExecuteAsync("sp_schedule_execute", new { RunId = inserted.Id });

        return new ScheduleStartResult
        {
            RunId = inserted.Id,
            Message = $"Run {inserted.Id} queued for '{schedule.Name}'.",
        };
    }
}
