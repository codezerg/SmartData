using SmartData.Server.Entities;

namespace SmartData.Server.Scheduling.Attributes;

/// <summary>
/// Fires weekly at the given time, on the days selected. <c>Every</c> defaults to 1
/// (every week); set to 2 for biweekly, etc.
/// </summary>
public sealed class WeeklyAttribute : ScheduleAttribute
{
    public Days Days { get; }
    public string? Time { get; }
    public int? Hour { get; }
    public int? Minute { get; }
    public int Every { get; set; } = 1;

    public WeeklyAttribute(Days days, string time) { Days = days; Time = time; }

    public WeeklyAttribute(Days days, int hour, int minute)
    {
        Days = days;
        Hour = hour;
        Minute = minute;
    }

    internal override SysSchedule Materialize()
    {
        if (Days == Scheduling.Days.None)
            throw new InvalidOperationException("[Weekly] requires at least one day.");
        if (Every <= 0)
            throw new InvalidOperationException("[Weekly] Every must be positive.");

        var t = Time != null
            ? ParseTime(Time, nameof(WeeklyAttribute))
            : new TimeSpan(Hour ?? 0, Minute ?? 0, 0);

        return new SysSchedule
        {
            Name          = Name ?? $"Weekly_{DaysSlug(Days)}_{TimeSlug(t)}",
            Enabled       = Enabled,
            FreqType      = "Weekly",
            FreqInterval  = Every,
            TimeOfDay     = t,
            DaysOfWeekMask = DaysMask(Days),
            MonthsMask    = Months == Scheduling.Months.All ? 0 : MonthsMask(Months),
            JitterSeconds = JitterSeconds,
            StartDate     = ResolveStartDate(),
            EndDate       = ResolveEndDate(),
        };
    }
}
