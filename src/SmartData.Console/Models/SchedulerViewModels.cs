using SmartData.Contracts;

namespace SmartData.Console.Models;

public class SchedulerListViewModel
{
    public List<ScheduleListItem> Schedules { get; set; } = [];
    public string? Search { get; set; }
    public string? Filter { get; set; }  // "all" | "enabled" | "disabled"
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SchedulerDetailViewModel
{
    public ScheduleGetResult Schedule { get; set; } = new();
    public List<DateTime> Preview { get; set; } = [];
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SchedulerHistoryViewModel
{
    public List<ScheduleRunItem> Runs { get; set; } = [];
    public string? Outcome { get; set; }
    public string? ProcedureName { get; set; }
    public int Limit { get; set; } = 100;
}

public class SchedulerStatsViewModel
{
    public ScheduleStatsResult Stats { get; set; } = new();
}
