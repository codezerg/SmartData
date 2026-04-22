using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;

namespace SmartData.Server.SystemProcedures.Scheduling;

internal class SpSchedulePreview : SystemAsyncStoredProcedure<SchedulePreviewResult>
{
    public int Id { get; set; }
    public int Count { get; set; } = 10;

    public override async Task<SchedulePreviewResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerList);
        db.UseDatabase("master");

        var row = await db.GetTable<SysSchedule>().FirstOrDefaultAsync(s => s.Id == Id, ct);
        if (row == null) RaiseError(2101, $"Schedule {Id} not found.");

        var result = new SchedulePreviewResult { ScheduleId = Id };
        var anchor = DateTime.Now;
        var n = Math.Clamp(Count, 1, 50);

        for (var i = 0; i < n; i++)
        {
            var next = SlotComputer.NextFire(row!, anchor);
            if (!next.HasValue) break;
            result.NextFireTimes.Add(next.Value);
            anchor = next.Value;
        }
        return result;
    }
}
