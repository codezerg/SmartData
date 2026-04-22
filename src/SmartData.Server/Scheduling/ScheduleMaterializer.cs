using System.Reflection;
using Microsoft.Extensions.Logging;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Scheduling.Attributes;

namespace SmartData.Server.Scheduling;

/// <summary>
/// Shared helpers for turning scheduled-procedure attributes into <see cref="SysSchedule"/>
/// rows and enforcing the sub-poll-interval guard. Used by <see cref="ScheduleReconciler"/>
/// at startup to materialize code-defined schedules into the DB.
/// </summary>
internal static class ScheduleMaterializer
{
    public static List<SysSchedule> BuildTargets(ProcedureCatalog catalog, ILogger logger)
    {
        var list = new List<SysSchedule>();
        foreach (var (procName, type) in catalog.GetAll())
        {
            var scheduleAttrs = type.GetCustomAttributes<ScheduleAttribute>(false).ToList();
            if (scheduleAttrs.Count == 0) continue;

            var retry = type.GetCustomAttribute<RetryAttribute>();
            var job = type.GetCustomAttribute<JobAttribute>();

            var materialized = new List<SysSchedule>();
            foreach (var attr in scheduleAttrs)
            {
                SysSchedule row;
                try { row = attr.Materialize(); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Schedule materialize failed for {Type}", type.FullName);
                    continue;
                }

                row.ProcedureName = procName;
                if (retry != null)
                {
                    row.RetryAttempts = retry.Attempts;
                    row.RetryIntervalSeconds = retry.IntervalSeconds;
                }
                if (job?.OwnerUser != null && !string.IsNullOrWhiteSpace(job.OwnerUser))
                    row.OwnerUser = job.OwnerUser;

                materialized.Add(row);
            }

            // Disambiguate duplicate names in declaration order: first keeps clean name,
            // duplicates get "_2", "_3", …
            var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in materialized)
            {
                var baseName = row.Name;
                if (!nameCounts.TryAdd(baseName, 1))
                {
                    nameCounts[baseName]++;
                    row.Name = $"{baseName}_{nameCounts[baseName]}";
                }
            }

            list.AddRange(materialized);
        }
        return list;
    }

    public static void ApplySubPollGuard(SysSchedule row, TimeSpan pollInterval, ILogger logger)
    {
        if (!row.Enabled) return;
        var min = MinInterval(row);
        if (min is null) return;
        if (min.Value >= pollInterval) return;

        logger.LogWarning(
            "Schedule '{Proc}.{Name}' has interval {Min} < PollInterval {Poll} — disabled.",
            row.ProcedureName, row.Name, min.Value, pollInterval);
        row.Enabled = false;
    }

    private static TimeSpan? MinInterval(SysSchedule row)
    {
        if (row.FreqType != "Every" || row.FreqUnit == null) return null;
        if (!Enum.TryParse<Unit>(row.FreqUnit, out var u)) return null;
        return u switch
        {
            Unit.Seconds => TimeSpan.FromSeconds(row.FreqInterval),
            Unit.Minutes => TimeSpan.FromMinutes(row.FreqInterval),
            Unit.Hours   => TimeSpan.FromHours(row.FreqInterval),
            Unit.Days    => TimeSpan.FromDays(row.FreqInterval),
            _ => (TimeSpan?)null,
        };
    }
}
