namespace SmartData.Contracts;

public class ScheduleListItem
{
    public int Id { get; set; }
    public string ProcedureName { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public string FreqType { get; set; } = "";
    public string FreqSummary { get; set; } = "";
    public DateTime? NextRunOn { get; set; }
    public DateTime? LastRunOn { get; set; }
    public string? LastOutcome { get; set; }
    public int RetryAttempts { get; set; }
    public int RetryIntervalSeconds { get; set; }
    public string OwnerUser { get; set; } = "";
    public string? Category { get; set; }
    public string? Description { get; set; }
}
