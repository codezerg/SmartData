namespace SmartData.Contracts;

public class ScheduleListResult
{
    public List<ScheduleListItem> Items { get; set; } = new();
    public int Total { get; set; }
}
