namespace SmartData.Contracts;

public class ScheduleHistoryResult
{
    public List<ScheduleRunItem> Items { get; set; } = new();
    public int Total { get; set; }
}
