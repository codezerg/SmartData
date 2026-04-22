using LinqToDB;
using Microsoft.Extensions.Options;
using SmartData.Core;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;
using SmartData.Server.Scheduling.Attributes;

namespace SmartData.Server.SystemProcedures.Scheduling;

/// <summary>
/// Built-in retention job: deletes <c>SysScheduleRun</c> rows older than
/// <c>SchedulerOptions.HistoryRetentionDays</c>. Wired via the scheduler itself —
/// the reconciler picks up its <c>[Daily]</c> attribute on startup.
/// </summary>
[Job("Schedule Run Retention", Category = "Scheduler",
     Description = "Deletes schedule run history older than HistoryRetentionDays.")]
[Daily("03:15")]
[AllowAnonymous]
internal class SpScheduleRunRetention : SystemAsyncStoredProcedure<VoidResult>
{
    private readonly SchedulerOptions _options;

    public SpScheduleRunRetention(IOptions<SchedulerOptions> options) { _options = options.Value; }

    public override async Task<VoidResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        db.UseDatabase("master");

        var cutoff = DateTime.Now.AddDays(-Math.Max(1, _options.HistoryRetentionDays));
        await db.DeleteAsync<SysScheduleRun>(r => r.StartedOn < cutoff, ct);
        return VoidResult.Instance;
    }
}
