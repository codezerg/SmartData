namespace SmartData.Contracts;

public class ScheduleRunItem
{
    public long Id { get; set; }
    public int ScheduleId { get; set; }
    public string ProcedureName { get; set; } = "";
    public string ScheduleName { get; set; } = "";
    public DateTime ScheduledFireTime { get; set; }
    public DateTime StartedOn { get; set; }
    public DateTime? FinishedOn { get; set; }
    public long DurationMs { get; set; }
    public string Outcome { get; set; } = "";
    public string? Message { get; set; }
    public int? ErrorId { get; set; }
    public int? ErrorSeverity { get; set; }
    public int AttemptNumber { get; set; }
    public string RunBy { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public bool CancelRequested { get; set; }
    public DateTime? LastHeartbeatAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
}
