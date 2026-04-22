namespace SmartData.Server.Scheduling.Attributes;

/// <summary>
/// Optional code-only metadata attached to a schedulable procedure. Never persisted —
/// <c>sp_schedule_list</c> reads these via reflection when presenting schedules.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class JobAttribute : Attribute
{
    public JobAttribute(string? name = null) { Name = name; }

    public string? Name { get; }
    public string Category { get; set; } = "General";
    public string? Description { get; set; }
    public string OwnerUser { get; set; } = "scheduler";
}
