using System.Globalization;
using SmartData.Server.Entities;

namespace SmartData.Server.Scheduling.Attributes;

/// <summary>
/// Base class for schedule-trigger attributes. Concrete subclasses materialize into
/// a <see cref="SysSchedule"/> row via <see cref="Materialize"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public abstract class ScheduleAttribute : Attribute
{
    /// <summary>Optional explicit schedule name. If null, a payload-derived slug is used.</summary>
    public string? Name { get; set; }

    /// <summary>Seeds the initial <see cref="SysSchedule.Enabled"/> value.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Optional "yyyy-MM-dd" — parsed at startup.</summary>
    public string? StartDate { get; set; }

    /// <summary>Optional "yyyy-MM-dd" — parsed at startup.</summary>
    public string? EndDate { get; set; }

    /// <summary>Random delay in seconds added to each fire.</summary>
    public int JitterSeconds { get; set; }

    /// <summary>Month filter. Defaults to <see cref="Months.All"/> (no restriction).</summary>
    public Months Months { get; set; } = Months.All;

    internal abstract SysSchedule Materialize();

    /// <summary>Parses "HH:mm" into a <see cref="TimeSpan"/>. Throws at startup on malformed input.</summary>
    protected static TimeSpan ParseTime(string value, string field)
    {
        if (!TimeSpan.TryParseExact(value, @"h\:mm", CultureInfo.InvariantCulture, out var ts) &&
            !TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out ts))
        {
            throw new FormatException($"{field}: expected \"HH:mm\", got \"{value}\".");
        }
        return ts;
    }

    /// <summary>Parses "HH:mm-HH:mm" into start/end. Throws on malformed input.</summary>
    protected static (TimeSpan start, TimeSpan end) ParseBetween(string value, string field)
    {
        var parts = value.Split('-');
        if (parts.Length != 2)
            throw new FormatException($"{field}: expected \"HH:mm-HH:mm\", got \"{value}\".");
        return (ParseTime(parts[0].Trim(), field), ParseTime(parts[1].Trim(), field));
    }

    /// <summary>Parses an optional yyyy-MM-dd date string. Returns <c>DateTime.MinValue</c> for null.</summary>
    protected DateTime ResolveStartDate() => ParseDate(StartDate) ?? new DateTime(2000, 1, 1);

    /// <summary>Parses <see cref="EndDate"/> into a DateTime or null.</summary>
    protected DateTime? ResolveEndDate() => ParseDate(EndDate);

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt;
        throw new FormatException($"Date: expected yyyy-MM-dd, got \"{value}\".");
    }

    /// <summary>Renders a time value as a slug fragment: "09_00".</summary>
    protected static string TimeSlug(TimeSpan ts) =>
        $"{ts.Hours:D2}_{ts.Minutes:D2}";

    /// <summary>Renders a <see cref="Days"/> bitmask as "Mon" or "Mon-Wed-Fri" etc.</summary>
    protected static string DaysSlug(Days days)
    {
        if (days == Days.None) return "None";
        if (days == Days.Weekdays) return "Weekdays";
        if (days == Days.Weekends) return "Weekends";
        if (days == Days.All) return "All";
        var names = new List<string>();
        if ((days & Days.Mon) != 0) names.Add("Mon");
        if ((days & Days.Tue) != 0) names.Add("Tue");
        if ((days & Days.Wed) != 0) names.Add("Wed");
        if ((days & Days.Thu) != 0) names.Add("Thu");
        if ((days & Days.Fri) != 0) names.Add("Fri");
        if ((days & Days.Sat) != 0) names.Add("Sat");
        if ((days & Days.Sun) != 0) names.Add("Sun");
        return string.Join("-", names);
    }

    /// <summary>Renders a <see cref="Weeks"/> bitmask as "First" or "First-Last" etc.</summary>
    protected static string WeeksSlug(Weeks weeks)
    {
        if (weeks == Weeks.None) return "None";
        if (weeks == Weeks.All) return "All";
        var names = new List<string>();
        if ((weeks & Weeks.First) != 0) names.Add("First");
        if ((weeks & Weeks.Second) != 0) names.Add("Second");
        if ((weeks & Weeks.Third) != 0) names.Add("Third");
        if ((weeks & Weeks.Fourth) != 0) names.Add("Fourth");
        if ((weeks & Weeks.Last) != 0) names.Add("Last");
        return string.Join("-", names);
    }

    /// <summary>Renders a <see cref="Day"/> bitmask as "01" or "01-15" or "Last" etc.</summary>
    protected static string DaySlug(Day days)
    {
        if (days == Day.None) return "None";
        if (days == Day.Last) return "Last";
        var names = new List<string>();
        for (var i = 0; i < 31; i++)
        {
            if (((int)days & (1 << i)) != 0) names.Add((i + 1).ToString("D2"));
        }
        if (((int)days & unchecked((int)0x80000000)) != 0) names.Add("Last");
        return string.Join("-", names);
    }

    /// <summary>Converts a <see cref="Days"/> bitmask into the DB column bitmask (same shape).</summary>
    internal static int DaysMask(Days days) => (int)days;

    /// <summary>Converts a <see cref="Months"/> bitmask — DB stores same shape.</summary>
    internal static int MonthsMask(Months months) => (int)months;

    /// <summary>Converts a <see cref="Weeks"/> bitmask — DB stores same shape.</summary>
    internal static int WeeksMask(Weeks weeks) => (int)weeks;

    /// <summary>Converts a <see cref="Day"/> bitmask — DB stores same shape.</summary>
    internal static int DayMask(Day days) => (int)days;
}
