using LinqToDB;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;

namespace SmartData.Server.Scheduling;

/// <summary>
/// Walks the procedure catalog for types decorated with
/// <see cref="Attributes.ScheduleAttribute"/>, materializes their schedules, and merges
/// them into the <c>_sys_schedules</c> table. Runs synchronously at application start,
/// before the <see cref="JobScheduler"/> hosted service begins ticking.
/// </summary>
public sealed class ScheduleReconciler
{
    private readonly IServiceProvider _sp;
    private readonly ProcedureCatalog _catalog;
    private readonly SchedulerOptions _options;
    private readonly ILogger<ScheduleReconciler> _logger;

    public ScheduleReconciler(
        IServiceProvider sp,
        ProcedureCatalog catalog,
        IOptions<SchedulerOptions> options,
        ILogger<ScheduleReconciler> logger)
    {
        _sp = sp;
        _catalog = catalog;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
        ctx.UseDatabase("master");

        var existing = await ctx.GetTable<SysSchedule>().ToListAsync(ct);
        var existingByKey = existing.ToDictionary(s => Key(s.ProcedureName, s.Name), StringComparer.OrdinalIgnoreCase);

        var targets = ScheduleMaterializer.BuildTargets(_catalog, _logger);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;

        foreach (var target in targets)
        {
            var key = Key(target.ProcedureName, target.Name);
            seenKeys.Add(key);

            if (!existingByKey.TryGetValue(key, out var row))
            {
                target.CreatedOn = now;
                target.CreatedBy = "reconciler";
                target.NextRunOn = SlotComputer.NextFire(target, now);
                ScheduleMaterializer.ApplySubPollGuard(target, _options.PollInterval, _logger);
                await ctx.InsertAsync(target, ct);
                _logger.LogInformation("Schedule created: {Proc}.{Name} ({Freq})", target.ProcedureName, target.Name, target.FreqType);
                continue;
            }

            // Code owns timing. Overwrite timing fields unconditionally.
            // Preserve user-controlled fields: Enabled (toggle), RetryAttempts,
            // RetryIntervalSeconds, JitterSeconds (ops tuning).
            row.FreqType          = target.FreqType;
            row.FreqInterval      = target.FreqInterval;
            row.FreqUnit          = target.FreqUnit;
            row.DaysOfWeekMask    = target.DaysOfWeekMask;
            row.DaysOfMonthMask   = target.DaysOfMonthMask;
            row.WeeksOfMonthMask  = target.WeeksOfMonthMask;
            row.MonthsMask        = target.MonthsMask;
            row.TimeOfDay         = target.TimeOfDay;
            row.BetweenStart      = target.BetweenStart;
            row.BetweenEnd        = target.BetweenEnd;
            row.RunOnce           = target.RunOnce;
            row.StartDate         = target.StartDate;
            row.EndDate           = target.EndDate;
            row.OwnerUser         = target.OwnerUser;
            row.ModifiedOn        = now;
            row.ModifiedBy        = "reconciler";
            row.NextRunOn         = SlotComputer.NextFire(row, now);
            ScheduleMaterializer.ApplySubPollGuard(row, _options.PollInterval, _logger);
            await ctx.UpdateAsync(row, ct);
        }

        // Disable rows whose attributes have been removed. Row is kept so
        // run history remains linked and viewable.
        foreach (var row in existing)
        {
            var key = Key(row.ProcedureName, row.Name);
            if (seenKeys.Contains(key)) continue;
            if (!row.Enabled) continue;

            row.Enabled = false;
            row.ModifiedOn = now;
            row.ModifiedBy = "reconciler";
            await ctx.UpdateAsync(row, ct);
            _logger.LogInformation("Schedule disabled (attribute removed): {Proc}.{Name}", row.ProcedureName, row.Name);
        }
    }

    private static string Key(string procedure, string name) => $"{procedure}::{name}";
}
