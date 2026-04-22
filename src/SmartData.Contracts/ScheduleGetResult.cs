namespace SmartData.Contracts;

public class ScheduleGetResult
{
    public int Id { get; set; }
    public string ProcedureName { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }

    public string FreqType { get; set; } = "";
    public int FreqInterval { get; set; }
    public string? FreqUnit { get; set; }
    public int DaysOfWeekMask { get; set; }
    public int DaysOfMonthMask { get; set; }
    public int WeeksOfMonthMask { get; set; }
    public int MonthsMask { get; set; }
    public TimeSpan? TimeOfDay { get; set; }
    public TimeSpan? BetweenStart { get; set; }
    public TimeSpan? BetweenEnd { get; set; }
    public int JitterSeconds { get; set; }
    public DateTime? RunOnce { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime? NextRunOn { get; set; }
    public DateTime? LastRunOn { get; set; }

    public int RetryAttempts { get; set; }
    public int RetryIntervalSeconds { get; set; }

    public string OwnerUser { get; set; } = "";

    public string? Category { get; set; }
    public string? Description { get; set; }

    public DateTime CreatedOn { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? ModifiedOn { get; set; }
    public string? ModifiedBy { get; set; }

    public List<ScheduleRunItem> RecentRuns { get; set; } = new();
}
