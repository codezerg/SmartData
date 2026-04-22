using SmartData.Server.Entities;

namespace SmartData.Server.Scheduling.Attributes;

/// <summary>
/// Fires on specific calendar days of the month (<see cref="Day.D1"/>, <see cref="Day.Last"/>, …).
/// Days that don't exist in a month (e.g. <see cref="Day.D31"/> in February) are skipped.
/// </summary>
public sealed class MonthlyAttribute : ScheduleAttribute
{
    public Day Days { get; }
    public string? Time { get; }
    public int? Hour { get; }
    public int? Minute { get; }

    public MonthlyAttribute(Day days, string time) { Days = days; Time = time; }

    public MonthlyAttribute(Day days, int hour, int minute)
    {
        Days = days;
        Hour = hour;
        Minute = minute;
    }

    internal override SysSchedule Materialize()
    {
        if (Days == Day.None) throw new InvalidOperationException("[Monthly] requires at least one day.");

        var t = Time != null
            ? ParseTime(Time, nameof(MonthlyAttribute))
            : new TimeSpan(Hour ?? 0, Minute ?? 0, 0);

        return new SysSchedule
        {
            Name          = Name ?? $"Monthly_{DaySlug(Days)}_{TimeSlug(t)}",
            Enabled       = Enabled,
            FreqType      = "Monthly",
            FreqInterval  = 1,
            TimeOfDay     = t,
            DaysOfMonthMask = DayMask(Days),
            MonthsMask    = Months == Scheduling.Months.All ? 0 : MonthsMask(Months),
            JitterSeconds = JitterSeconds,
            StartDate     = ResolveStartDate(),
            EndDate       = ResolveEndDate(),
        };
    }
}
