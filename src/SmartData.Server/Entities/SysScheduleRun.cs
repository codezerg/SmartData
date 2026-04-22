using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

namespace SmartData.Server.Entities;

/// <summary>
/// One row per fire attempt. Serves as both audit history and multi-instance
/// concurrency lock — the unique index on (ScheduleId, ScheduledFireTime, AttemptNumber)
/// means a double-claim fails on INSERT.
/// </summary>
[Table("_sys_schedule_runs")]
[Index("IX_ScheduleRun_Claim", nameof(ScheduleId), nameof(ScheduledFireTime), nameof(AttemptNumber), Unique = true)]
[Index("IX_ScheduleRun_Sched", nameof(ScheduleId), nameof(StartedOn))]
[Index("IX_ScheduleRun_Outcome", nameof(Outcome), nameof(StartedOn))]
[Index("IX_ScheduleRun_RetryScan", nameof(Outcome), nameof(NextAttemptAt))]
[Index("IX_ScheduleRun_Orphan", nameof(Outcome), nameof(LastHeartbeatAt))]
internal class SysScheduleRun
{
    [PrimaryKey, Identity]
    [Column] public long Id { get; set; }

    [Column] public int ScheduleId { get; set; }

    /// <summary>
    /// Ideal fire time this run represents. With (ScheduleId, AttemptNumber) forms the lock.
    /// First attempt = schedule time. Retry = retry-due time. Manual = DateTime.Now.
    /// </summary>
    [Column] public DateTime ScheduledFireTime { get; set; }

    [Column, MaxLength(128)] public string InstanceId { get; set; } = "";

    [Column] public DateTime StartedOn { get; set; }
    [Column, Nullable] public DateTime? FinishedOn { get; set; }
    [Column] public long DurationMs { get; set; }

    /// <summary>Claimed | Running | Succeeded | Failed | Cancelled.</summary>
    [Column, MaxLength(16)] public string Outcome { get; set; } = "Claimed";

    [Column, Nullable] public string? Message { get; set; }

    /// <summary>From ProcedureException.MessageId.</summary>
    [Column, Nullable] public int? ErrorId { get; set; }

    /// <summary>From ProcedureException.Severity.</summary>
    [Column, Nullable] public int? ErrorSeverity { get; set; }

    [Column] public int AttemptNumber { get; set; } = 1;

    [Column, MaxLength(64)] public string RunBy { get; set; } = "scheduler";

    /// <summary>
    /// Cooperative cancel signal. <c>sp_schedule_cancel</c> flips this; the in-flight
    /// <c>sp_schedule_execute</c> polls its own row and throws <c>OperationCanceledException</c>.
    /// </summary>
    [Column] public bool CancelRequested { get; set; }

    /// <summary>
    /// Liveness signal — bumped every <c>HeartbeatInterval</c> by the running instance.
    /// Orphan sweep uses this (not <c>StartedOn</c>), so long-running jobs aren't mistaken for crashes.
    /// </summary>
    [Column, Nullable] public DateTime? LastHeartbeatAt { get; set; }

    /// <summary>
    /// Retry queue. If non-null, the tick treats this row as a pending retry and fires
    /// a NEW ScheduleRun with AttemptNumber + 1 at this time.
    /// </summary>
    [Column, Nullable] public DateTime? NextAttemptAt { get; set; }
}
