using System.Reflection;
using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling.Attributes;

namespace SmartData.Server.SystemProcedures.Scheduling;

internal class SpScheduleList : SystemAsyncStoredProcedure<ScheduleListResult>
{
    public string? Search { get; set; }
    public bool? Enabled { get; set; }

    private readonly ProcedureCatalog _catalog;

    public SpScheduleList(ProcedureCatalog catalog) { _catalog = catalog; }

    public override async Task<ScheduleListResult> ExecuteAsync(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.SchedulerList);
        db.UseDatabase("master");

        var q = db.GetTable<SysSchedule>().AsQueryable();
        if (Enabled.HasValue) q = q.Where(s => s.Enabled == Enabled.Value);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            q = q.Where(s => s.Name.Contains(term) || s.ProcedureName.Contains(term));
        }

        var rows = await q.OrderBy(s => s.ProcedureName).ThenBy(s => s.Name).ToListAsync(ct);

        // Last outcome lookup
        var ids = rows.Select(r => r.Id).ToList();
        var lastOutcomes = new Dictionary<int, string>();
        if (ids.Count > 0)
        {
            var runs = await db.GetTable<SysScheduleRun>()
                .Where(r => ids.Contains(r.ScheduleId))
                .OrderByDescending(r => r.StartedOn)
                .Select(r => new { r.ScheduleId, r.Outcome, r.StartedOn })
                .ToListAsync(ct);
            foreach (var g in runs.GroupBy(r => r.ScheduleId))
                lastOutcomes[g.Key] = g.First().Outcome;
        }

        var items = rows.Select(r => new ScheduleListItem
        {
            Id = r.Id,
            ProcedureName = r.ProcedureName,
            Name = r.Name,
            Enabled = r.Enabled,
            FreqType = r.FreqType,
            FreqSummary = Summarize(r),
            NextRunOn = r.NextRunOn,
            LastRunOn = r.LastRunOn,
            LastOutcome = lastOutcomes.TryGetValue(r.Id, out var o) ? o : null,
            RetryAttempts = r.RetryAttempts,
            RetryIntervalSeconds = r.RetryIntervalSeconds,
            OwnerUser = r.OwnerUser,
            Category = GetJobCategory(r.ProcedureName),
            Description = GetJobDescription(r.ProcedureName),
        }).ToList();

        return new ScheduleListResult { Items = items, Total = items.Count };
    }

    private string? GetJobCategory(string procName) =>
        _catalog.GetAll().TryGetValue(procName, out var t)
            ? t.GetCustomAttribute<JobAttribute>()?.Category : null;

    private string? GetJobDescription(string procName) =>
        _catalog.GetAll().TryGetValue(procName, out var t)
            ? t.GetCustomAttribute<JobAttribute>()?.Description : null;

    private static string Summarize(SysSchedule s) => s.FreqType switch
    {
        "OneTime"    => $"Once @ {s.RunOnce:yyyy-MM-dd HH:mm}",
        "Every"      => $"Every {s.FreqInterval} {s.FreqUnit}".ToLowerInvariant(),
        "Daily"      => $"Daily @ {s.TimeOfDay:hh\\:mm}",
        "Weekly"     => $"Weekly @ {s.TimeOfDay:hh\\:mm} (mask {s.DaysOfWeekMask})",
        "Monthly"    => $"Monthly @ {s.TimeOfDay:hh\\:mm} (days mask {s.DaysOfMonthMask})",
        "MonthlyDow" => $"MonthlyDow @ {s.TimeOfDay:hh\\:mm}",
        _ => s.FreqType,
    };
}
