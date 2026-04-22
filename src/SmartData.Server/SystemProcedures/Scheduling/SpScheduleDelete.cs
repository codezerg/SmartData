using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures.Scheduling;

internal class SpScheduleDelete : SystemAsyncStoredProcedure<ScheduleDeleteResult>
{
    public int Id { get; set; }

    public override async Task<ScheduleDeleteResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerEdit);
        db.UseDatabase("master");

        var row = await db.GetTable<SysSchedule>().FirstOrDefaultAsync(s => s.Id == Id, ct);
        if (row == null) RaiseError(2101, $"Schedule {Id} not found.");

        await db.DeleteAsync<SysScheduleRun>(r => r.ScheduleId == Id, ct);
        await db.DeleteAsync(row!, ct);

        return new ScheduleDeleteResult { Message = $"Schedule '{row!.Name}' deleted." };
    }
}
