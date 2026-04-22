using SmartData.Server.Entities;

namespace SmartData.Server.Scheduling;

/// <summary>
/// Pure function: given a <see cref="SysSchedule"/> row and an anchor <see cref="DateTime"/>,
/// returns the next strictly-greater-than-anchor fire time, or <c>null</c> when exhausted.
/// All math runs in server local time — the caller supplies <see cref="DateTime.Now"/>.
/// <see cref="SysSchedule.JitterSeconds"/> is applied by the caller at claim time, not here.
/// </summary>
internal static class SlotComputer
{
    private const int SafetyIterDays = 366 * 4;

    public static DateTime? NextFire(SysSchedule s, DateTime anchor)
    {
        if (s.EndDate.HasValue && anchor >= s.EndDate.Value) return null;
        if (s.StartDate > anchor) anchor = s.StartDate.AddTicks(-1);

        var candidate = s.FreqType switch
        {
            "OneTime"    => NextOneTime(s, anchor),
            "Every"      => NextEvery(s, anchor),
            "Daily"      => NextDaily(s, anchor),
            "Weekly"     => NextWeekly(s, anchor),
            "Monthly"    => NextMonthly(s, anchor),
            "MonthlyDow" => NextMonthlyDow(s, anchor),
            _ => (DateTime?)null
        };

        if (candidate.HasValue && s.EndDate.HasValue && candidate.Value > s.EndDate.Value)
            return null;

        return candidate;
    }

    private static DateTime? NextOneTime(SysSchedule s, DateTime anchor)
    {
        if (!s.RunOnce.HasValue) return null;
        return s.RunOnce.Value > anchor ? s.RunOnce.Value : null;
    }

    private static DateTime? NextEvery(SysSchedule s, DateTime anchor)
    {
        if (s.FreqUnit == null || !Enum.TryParse<Unit>(s.FreqUnit, out var unit)) return null;
        double slotSec = unit switch
        {
            Unit.Seconds => 1,
            Unit.Minutes => 60,
            Unit.Hours   => 3600,
            Unit.Days    => 86400,
            _ => 0,
        };
        slotSec *= Math.Max(1, s.FreqInterval);
        if (slotSec <= 0) return null;

        var day = anchor.Date;
        var tod = (anchor - day).TotalSeconds;
        var nextSec = Math.Ceiling(tod / slotSec) * slotSec;
        var candidate = day.AddSeconds(nextSec);
        if (candidate <= anchor) candidate = candidate.AddSeconds(slotSec);

        var maxIter = slotSec <= 60 ? 100_000 : (int)Math.Max(1000, 366L * 86400 / (long)slotSec);
        for (var i = 0; i < maxIter; i++)
        {
            if (MatchesMonth(s.MonthsMask, candidate) && MatchesDayOfWeek(s.DaysOfWeekMask, candidate))
            {
                if (s.BetweenStart.HasValue && s.BetweenEnd.HasValue)
                {
                    var t = candidate - candidate.Date;
                    if (t < s.BetweenStart.Value)
                    {
                        candidate = candidate.Date.Add(s.BetweenStart.Value);
                        var sec = (candidate - candidate.Date).TotalSeconds;
                        candidate = candidate.Date.AddSeconds(Math.Ceiling(sec / slotSec) * slotSec);
                        continue;
                    }
                    if (t >= s.BetweenEnd.Value)
                    {
                        candidate = candidate.Date.AddDays(1).Add(s.BetweenStart.Value);
                        continue;
                    }
                }
                return candidate;
            }
            candidate = candidate.AddSeconds(slotSec);
        }
        return null;
    }

    private static DateTime? NextDaily(SysSchedule s, DateTime anchor)
    {
        var tod = s.TimeOfDay ?? TimeSpan.Zero;
        var candidate = anchor.Date.Add(tod);
        if (candidate <= anchor) candidate = candidate.AddDays(1);
        for (var i = 0; i < SafetyIterDays; i++)
        {
            if (MatchesMonth(s.MonthsMask, candidate) && MatchesDayOfWeek(s.DaysOfWeekMask, candidate))
                return candidate;
            candidate = candidate.AddDays(1);
        }
        return null;
    }

    private static DateTime? NextWeekly(SysSchedule s, DateTime anchor)
    {
        var tod = s.TimeOfDay ?? TimeSpan.Zero;
        var interval = Math.Max(1, s.FreqInterval);
        var candidate = anchor.Date.Add(tod);
        if (candidate <= anchor) candidate = candidate.Date.AddDays(1).Add(tod);
        var startWeek = StartOfWeek(s.StartDate.Date);
        for (var i = 0; i < SafetyIterDays; i++)
        {
            var weeks = (int)Math.Floor((StartOfWeek(candidate.Date) - startWeek).TotalDays / 7.0);
            var weekValid = weeks >= 0 && weeks % interval == 0;
            if (weekValid
                && MatchesMonth(s.MonthsMask, candidate)
                && MatchesDayOfWeek(s.DaysOfWeekMask, candidate))
                return candidate;
            candidate = candidate.AddDays(1);
        }
        return null;
    }

    private static DateTime? NextMonthly(SysSchedule s, DateTime anchor)
    {
        var tod = s.TimeOfDay ?? TimeSpan.Zero;
        if (s.DaysOfMonthMask == 0) return null;
        var candidate = anchor.Date.Add(tod);
        if (candidate <= anchor) candidate = candidate.Date.AddDays(1).Add(tod);
        for (var i = 0; i < SafetyIterDays; i++)
        {
            if (MatchesMonth(s.MonthsMask, candidate) && DayOfMonthMatches(s.DaysOfMonthMask, candidate))
                return candidate;
            candidate = candidate.AddDays(1);
        }
        return null;
    }

    private static DateTime? NextMonthlyDow(SysSchedule s, DateTime anchor)
    {
        var tod = s.TimeOfDay ?? TimeSpan.Zero;
        if (s.WeeksOfMonthMask == 0 || s.DaysOfWeekMask == 0) return null;
        var candidate = anchor.Date.Add(tod);
        if (candidate <= anchor) candidate = candidate.Date.AddDays(1).Add(tod);
        for (var i = 0; i < SafetyIterDays; i++)
        {
            if (MatchesMonth(s.MonthsMask, candidate)
                && MatchesDayOfWeek(s.DaysOfWeekMask, candidate)
                && WeekOfMonthMatches(s.WeeksOfMonthMask, candidate))
                return candidate;
            candidate = candidate.AddDays(1);
        }
        return null;
    }

    private static bool MatchesMonth(int mask, DateTime d)
        => mask == 0 || (mask & (1 << (d.Month - 1))) != 0;

    private static bool MatchesDayOfWeek(int mask, DateTime d)
        => mask == 0 || (mask & (1 << (int)d.DayOfWeek)) != 0;

    private static bool DayOfMonthMatches(int mask, DateTime d)
    {
        if ((mask & (1 << (d.Day - 1))) != 0) return true;
        var dim = DateTime.DaysInMonth(d.Year, d.Month);
        if (d.Day == dim && (mask & unchecked((int)0x80000000)) != 0) return true;
        return false;
    }

    private static bool WeekOfMonthMatches(int mask, DateTime d)
    {
        var occurrence = (d.Day - 1) / 7 + 1;
        var dim = DateTime.DaysInMonth(d.Year, d.Month);
        var isLast = d.Day + 7 > dim;
        if (occurrence >= 1 && occurrence <= 4 && (mask & (1 << (occurrence - 1))) != 0) return true;
        if (isLast && (mask & 16) != 0) return true;
        return false;
    }

    private static DateTime StartOfWeek(DateTime d)
    {
        var diff = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.Date.AddDays(-diff);
    }
}
