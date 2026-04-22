namespace SmartData.Contracts;

public class SchedulePreviewResult
{
    public int ScheduleId { get; set; }
    public List<DateTime> NextFireTimes { get; set; } = new();
}
