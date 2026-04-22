using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

namespace SmartData.Server.Entities;

/// <summary>
/// One row per timing rule. One procedure firing on one frequency.
/// Stacked schedule attributes on the same procedure produce multiple rows.
/// </summary>
[Table("_sys_schedules")]
[Index("IX_Schedule_Unique", nameof(ProcedureName), nameof(Name), Unique = true)]
[Index("IX_Schedule_Due", nameof(Enabled), nameof(NextRunOn))]
internal class SysSchedule
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }

    [Column, MaxLength(128)] public string ProcedureName { get; set; } = "";

    /// <summary>
    /// Per-procedure schedule name — stable match key for reconciliation.
    /// Default is derived from the attribute payload (e.g. "Daily_09_00").
    /// </summary>
    [Column, MaxLength(128)] public string Name { get; set; } = "";

    [Column] public bool Enabled { get; set; } = true;

    /// <summary>OneTime | Every | Daily | Weekly | Monthly | MonthlyDow.</summary>
    [Column, MaxLength(16)] public string FreqType { get; set; } = "Daily";

    /// <summary>Every N units (Every) or every N weeks (Weekly).</summary>
    [Column] public int FreqInterval { get; set; } = 1;

    /// <summary>Seconds | Minutes | Hours | Days — <c>Every</c> only.</summary>
    [Column, MaxLength(16), Nullable] public string? FreqUnit { get; set; }

    /// <summary>Bitmask. Sun=1, Mon=2, Tue=4, … Sat=64. 0 means no restriction.</summary>
    [Column] public int DaysOfWeekMask { get; set; }

    /// <summary>Bitmask. bit 0..30 = days 1..31, bit 31 = Last.</summary>
    [Column] public int DaysOfMonthMask { get; set; }

    /// <summary>Bitmask. bit 0..3 = weeks 1..4, bit 4 = Last. <c>MonthlyDow</c> only.</summary>
    [Column] public int WeeksOfMonthMask { get; set; }

    /// <summary>Bitmask. bit 0..11 = Jan..Dec. 0 means all months.</summary>
    [Column] public int MonthsMask { get; set; }

    /// <summary>Anchor time of day (Daily/Weekly/Monthly/MonthlyDow).</summary>
    [Column, Nullable] public TimeSpan? TimeOfDay { get; set; }

    /// <summary>Daily window start for repeating schedules (Every).</summary>
    [Column, Nullable] public TimeSpan? BetweenStart { get; set; }

    /// <summary>Daily window end for repeating schedules (Every).</summary>
    [Column, Nullable] public TimeSpan? BetweenEnd { get; set; }

    /// <summary>Random delay (seconds) added to each fire to avoid thundering-herd.</summary>
    [Column] public int JitterSeconds { get; set; }

    /// <summary>Fire-once timestamp, <c>OneTime</c> only.</summary>
    [Column, Nullable] public DateTime? RunOnce { get; set; }

    [Column] public DateTime StartDate { get; set; }
    [Column, Nullable] public DateTime? EndDate { get; set; }
    [Column, Nullable] public DateTime? NextRunOn { get; set; }
    [Column, Nullable] public DateTime? LastRunOn { get; set; }

    /// <summary>Total runs including the first. 1 = no retry.</summary>
    [Column] public int RetryAttempts { get; set; } = 1;

    [Column] public int RetryIntervalSeconds { get; set; } = 60;

    /// <summary>Identity used for permission checks when this schedule fires.</summary>
    [Column, MaxLength(256)] public string OwnerUser { get; set; } = "scheduler";

    [Column] public DateTime CreatedOn { get; set; }
    [Column, MaxLength(256)] public string CreatedBy { get; set; } = "";
    [Column, Nullable] public DateTime? ModifiedOn { get; set; }
    [Column, MaxLength(256), Nullable] public string? ModifiedBy { get; set; }
}
