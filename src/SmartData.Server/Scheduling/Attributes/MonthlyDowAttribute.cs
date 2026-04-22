using SmartData.Server.Entities;

namespace SmartData.Server.Scheduling.Attributes;

/// <summary>
/// Weekday-of-month cadence — "first Monday of every month" style.
/// Combines a <see cref="Weeks"/> selector (which week) with a <see cref="Days"/> selector (which weekday).
/// </summary>
public sealed class MonthlyDowAttribute : ScheduleAttribute
{
    public Weeks Weeks { get; }
    public Days Days { get; }
    public string? Time { get; }
    public int? Hour { get; }
    public int? Minute { get; }

    public MonthlyDowAttribute(Weeks weeks, Days days, string time)
    {
        Weeks = weeks;
        Days = days;
        Time = time;
    }

    public MonthlyDowAttribute(Weeks weeks, Days days, int hour, int minute)
    {
        Weeks = weeks;
        Days = days;
        Hour = hour;
        Minute = minute;
    }

    internal override SysSchedule Materialize()
    {
        if (Weeks == Scheduling.Weeks.None) throw new InvalidOperationException("[MonthlyDow] requires at least one week.");
        if (Days == Scheduling.Days.None) throw new InvalidOperationException("[MonthlyDow] requires at least one day.");

        var t = Time != null
            ? ParseTime(Time, nameof(MonthlyDowAttribute))
            : new TimeSpan(Hour ?? 0, Minute ?? 0, 0);

        return new SysSchedule
        {
            Name          = Name ?? $"MonthlyDow_{WeeksSlug(Weeks)}_{DaysSlug(Days)}_{TimeSlug(t)}",
            Enabled       = Enabled,
            FreqType      = "MonthlyDow",
            FreqInterval  = 1,
            TimeOfDay     = t,
            WeeksOfMonthMask = WeeksMask(Weeks),
            DaysOfWeekMask   = DaysMask(Days),
            MonthsMask    = Months == Scheduling.Months.All ? 0 : MonthsMask(Months),
            JitterSeconds = JitterSeconds,
            StartDate     = ResolveStartDate(),
            EndDate       = ResolveEndDate(),
        };
    }
}
