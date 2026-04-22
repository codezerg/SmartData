using LinqToDB;
using SmartData.Core;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures.Scheduling;

internal class SpScheduleCancel : SystemAsyncStoredProcedure<VoidResult>
{
    public int? ScheduleId { get; set; }
    public long? RunId { get; set; }

    public override async Task<VoidResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerCancel);
        db.UseDatabase("master");

        if (!ScheduleId.HasValue && !RunId.HasValue)
            RaiseError("Provide ScheduleId or RunId.");

        if (RunId.HasValue)
        {
            await db.GetTable<SysScheduleRun>()
                .Where(r => r.Id == RunId.Value && (r.Outcome == "Claimed" || r.Outcome == "Running"))
                .Set(r => r.CancelRequested, true)
                .UpdateAsync(ct);
        }
        else
        {
            await db.GetTable<SysScheduleRun>()
                .Where(r => r.ScheduleId == ScheduleId!.Value && (r.Outcome == "Claimed" || r.Outcome == "Running"))
                .Set(r => r.CancelRequested, true)
                .UpdateAsync(ct);
        }

        return VoidResult.Instance;
    }
}
