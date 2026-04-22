using System.Reflection;
using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling.Attributes;

namespace SmartData.Server.SystemProcedures.Scheduling;

internal class SpScheduleGet : SystemAsyncStoredProcedure<ScheduleGetResult>
{
    public int Id { get; set; }
    public int RecentRuns { get; set; } = 20;

    private readonly ProcedureCatalog _catalog;

    public SpScheduleGet(ProcedureCatalog catalog) { _catalog = catalog; }

    public override async Task<ScheduleGetResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerList);
        db.UseDatabase("master");

        var row = await db.GetTable<SysSchedule>().FirstOrDefaultAsync(s => s.Id == Id, ct);
        if (row == null) RaiseError(2101, $"Schedule {Id} not found.");

        var runs = await db.GetTable<SysScheduleRun>()
            .Where(r => r.ScheduleId == row!.Id)
            .OrderByDescending(r => r.StartedOn)
            .Take(Math.Max(1, RecentRuns))
            .ToListAsync(ct);

        var job = _catalog.GetAll().TryGetValue(row!.ProcedureName, out var t)
            ? t.GetCustomAttribute<JobAttribute>() : null;

        return new ScheduleGetResult
        {
            Id = row.Id,
            ProcedureName = row.ProcedureName,
            Name = row.Name,
            Enabled = row.Enabled,
            FreqType = row.FreqType,
            FreqInterval = row.FreqInterval,
            FreqUnit = row.FreqUnit,
            DaysOfWeekMask = row.DaysOfWeekMask,
            DaysOfMonthMask = row.DaysOfMonthMask,
            WeeksOfMonthMask = row.WeeksOfMonthMask,
            MonthsMask = row.MonthsMask,
            TimeOfDay = row.TimeOfDay,
            BetweenStart = row.BetweenStart,
            BetweenEnd = row.BetweenEnd,
            JitterSeconds = row.JitterSeconds,
            RunOnce = row.RunOnce,
            StartDate = row.StartDate,
            EndDate = row.EndDate,
            NextRunOn = row.NextRunOn,
            LastRunOn = row.LastRunOn,
            RetryAttempts = row.RetryAttempts,
            RetryIntervalSeconds = row.RetryIntervalSeconds,
            OwnerUser = row.OwnerUser,
            Category = job?.Category,
            Description = job?.Description,
            CreatedOn = row.CreatedOn,
            CreatedBy = row.CreatedBy,
            ModifiedOn = row.ModifiedOn,
            ModifiedBy = row.ModifiedBy,
            RecentRuns = runs.Select(r => ScheduleRunMapper.Map(r, row.ProcedureName, row.Name)).ToList(),
        };
    }
}

internal static class ScheduleRunMapper
{
    public static ScheduleRunItem Map(SysScheduleRun r, string procName, string schedName) => new()
    {
        Id = r.Id,
        ScheduleId = r.ScheduleId,
        ProcedureName = procName,
        ScheduleName = schedName,
        ScheduledFireTime = r.ScheduledFireTime,
        StartedOn = r.StartedOn,
        FinishedOn = r.FinishedOn,
        DurationMs = r.DurationMs,
        Outcome = r.Outcome,
        Message = r.Message,
        ErrorId = r.ErrorId,
        ErrorSeverity = r.ErrorSeverity,
        AttemptNumber = r.AttemptNumber,
        RunBy = r.RunBy,
        InstanceId = r.InstanceId,
        CancelRequested = r.CancelRequested,
        LastHeartbeatAt = r.LastHeartbeatAt,
        NextAttemptAt = r.NextAttemptAt,
    };
}
