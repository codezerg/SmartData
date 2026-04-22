using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures.Scheduling;

internal class SpScheduleHistory : SystemAsyncStoredProcedure<ScheduleHistoryResult>
{
    public int? ScheduleId { get; set; }
    public string? ProcedureName { get; set; }
    public string? Outcome { get; set; }
    public DateTime? Since { get; set; }
    public DateTime? Until { get; set; }
    public int Limit { get; set; } = 100;

    public override async Task<ScheduleHistoryResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerList);
        db.UseDatabase("master");

        var q = db.GetTable<SysScheduleRun>().AsQueryable();
        if (ScheduleId.HasValue) q = q.Where(r => r.ScheduleId == ScheduleId.Value);
        if (!string.IsNullOrWhiteSpace(Outcome)) q = q.Where(r => r.Outcome == Outcome);
        if (Since.HasValue) q = q.Where(r => r.StartedOn >= Since.Value);
        if (Until.HasValue) q = q.Where(r => r.StartedOn <= Until.Value);

        var joined = await (from r in q
                            join s in db.GetTable<SysSchedule>() on r.ScheduleId equals s.Id
                            where ProcedureName == null || s.ProcedureName == ProcedureName
                            orderby r.StartedOn descending
                            select new { Run = r, s.ProcedureName, SchedName = s.Name })
                          .Take(Math.Clamp(Limit, 1, 1000))
                          .ToListAsync(ct);

        var items = joined.Select(x => ScheduleRunMapper.Map(x.Run, x.ProcedureName, x.SchedName)).ToList();
        return new ScheduleHistoryResult { Items = items, Total = items.Count };
    }
}
