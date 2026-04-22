using System.Globalization;
using SmartData.Server.Entities;

namespace SmartData.Server.Scheduling.Attributes;

/// <summary>Fires once at the given local time. Schedule auto-disables after the run.</summary>
public sealed class OnceAttribute : ScheduleAttribute
{
    public string DateTime { get; }

    public OnceAttribute(string dateTime) { DateTime = dateTime; }

    internal override SysSchedule Materialize()
    {
        if (!System.DateTime.TryParse(
                DateTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var when))
        {
            throw new FormatException($"[Once]: expected parseable date-time, got \"{DateTime}\".");
        }

        var slug = when.ToString("yyyy_MM_dd_HH_mm", CultureInfo.InvariantCulture);

        return new SysSchedule
        {
            Name          = Name ?? $"Once_{slug}",
            Enabled       = Enabled,
            FreqType      = "OneTime",
            FreqInterval  = 1,
            RunOnce       = when,
            JitterSeconds = JitterSeconds,
            StartDate     = ResolveStartDate(),
            EndDate       = ResolveEndDate(),
        };
    }
}
