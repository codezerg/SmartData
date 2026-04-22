using SmartData.Server.Entities;

namespace SmartData.Server.Scheduling.Attributes;

/// <summary>Fires once per day at the given time. <c>Days</c> narrows to specific weekdays.</summary>
public sealed class DailyAttribute : ScheduleAttribute
{
    public string? Time { get; }
    public int? Hour { get; }
    public int? Minute { get; }

    public Days Days { get; set; } = Scheduling.Days.All;

    public DailyAttribute(string time) { Time = time; }

    public DailyAttribute(int hour, int minute) { Hour = hour; Minute = minute; }

    internal override SysSchedule Materialize()
    {
        var t = Time != null
            ? ParseTime(Time, nameof(DailyAttribute))
            : new TimeSpan(Hour ?? 0, Minute ?? 0, 0);

        return new SysSchedule
        {
            Name          = Name ?? $"Daily_{TimeSlug(t)}",
            Enabled       = Enabled,
            FreqType      = "Daily",
            FreqInterval  = 1,
            TimeOfDay     = t,
            DaysOfWeekMask = Days == Scheduling.Days.All ? 0 : DaysMask(Days),
            MonthsMask    = Months == Scheduling.Months.All ? 0 : MonthsMask(Months),
            JitterSeconds = JitterSeconds,
            StartDate     = ResolveStartDate(),
            EndDate       = ResolveEndDate(),
        };
    }
}
