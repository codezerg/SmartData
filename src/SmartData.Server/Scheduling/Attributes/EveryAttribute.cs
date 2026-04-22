using SmartData.Server.Entities;

namespace SmartData.Server.Scheduling.Attributes;

/// <summary>Fires on wall-clock anchors — <c>Every(5, Minutes)</c> fires at :00/:05/:10/…</summary>
public sealed class EveryAttribute : ScheduleAttribute
{
    public int Interval { get; }
    public Unit Unit { get; }

    /// <summary>Optional "HH:mm-HH:mm" window. Fires outside are skipped.</summary>
    public string? Between { get; set; }

    public Days Days { get; set; } = Scheduling.Days.All;

    public EveryAttribute(int interval, Unit unit)
    {
        if (interval <= 0) throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
        Interval = interval;
        Unit = unit;
    }

    internal override SysSchedule Materialize()
    {
        TimeSpan? betweenStart = null, betweenEnd = null;
        if (!string.IsNullOrWhiteSpace(Between))
        {
            var (s, e) = ParseBetween(Between, nameof(Between));
            betweenStart = s;
            betweenEnd = e;
        }

        var unitTag = Unit switch
        {
            Unit.Seconds => "s",
            Unit.Minutes => "m",
            Unit.Hours   => "h",
            Unit.Days    => "d",
            _ => "?"
        };

        return new SysSchedule
        {
            Name          = Name ?? $"Every_{Interval}{unitTag}",
            Enabled       = Enabled,
            FreqType      = "Every",
            FreqInterval  = Interval,
            FreqUnit      = Unit.ToString(),
            DaysOfWeekMask = Days == Scheduling.Days.All ? 0 : DaysMask(Days),
            MonthsMask    = Months == Scheduling.Months.All ? 0 : MonthsMask(Months),
            BetweenStart  = betweenStart,
            BetweenEnd    = betweenEnd,
            JitterSeconds = JitterSeconds,
            StartDate     = ResolveStartDate(),
            EndDate       = ResolveEndDate(),
        };
    }
}
