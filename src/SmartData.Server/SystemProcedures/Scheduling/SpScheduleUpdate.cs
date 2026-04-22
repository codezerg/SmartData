using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;

namespace SmartData.Server.SystemProcedures.Scheduling;

/// <summary>
/// Updates the user-controllable fields on a schedule: Enabled toggle and the
/// ops-tuning knobs (retry attempts, retry interval, jitter). Timing is owned
/// by code attributes and can only be changed by editing the attribute and
/// restarting — the reconciler overwrites timing fields on startup.
/// </summary>
internal class SpScheduleUpdate : SystemAsyncStoredProcedure<ScheduleSaveResult>
{
    public int Id { get; set; }

    public bool? Enabled { get; set; }
    public int? RetryAttempts { get; set; }
    public int? RetryIntervalSeconds { get; set; }
    public int? JitterSeconds { get; set; }

    public override async Task<ScheduleSaveResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerEdit);
        db.UseDatabase("master");

        var row = await db.GetTable<SysSchedule>().FirstOrDefaultAsync(s => s.Id == Id, ct);
        if (row == null) RaiseError(2101, $"Schedule {Id} not found.");

        if (Enabled.HasValue)              row!.Enabled = Enabled.Value;
        if (RetryAttempts.HasValue)        row!.RetryAttempts = Math.Max(1, RetryAttempts.Value);
        if (RetryIntervalSeconds.HasValue) row!.RetryIntervalSeconds = Math.Max(0, RetryIntervalSeconds.Value);
        if (JitterSeconds.HasValue)        row!.JitterSeconds = Math.Max(0, JitterSeconds.Value);

        row!.ModifiedOn = DateTime.Now;
        row.ModifiedBy = identity.UserId;
        row.NextRunOn = SlotComputer.NextFire(row, DateTime.Now);

        await db.UpdateAsync(row, ct);
        return new ScheduleSaveResult { Id = row.Id, Message = $"Schedule '{row.Name}' updated." };
    }
}
