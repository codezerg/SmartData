namespace SmartData.Server.Scheduling;

/// <summary>
/// Configuration for the scheduling subsystem. Nested under <c>SmartDataOptions.Scheduler</c>.
/// </summary>
public class SchedulerOptions
{
    /// <summary>Master on/off switch for the <c>JobScheduler</c> hosted service. Reconciliation runs regardless.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often <c>sp_scheduler_tick</c> runs.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Max concurrent in-flight <c>sp_schedule_execute</c> invocations across this instance.</summary>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary><c>ScheduleRun</c> rows older than this are deleted by the built-in retention schedule.</summary>
    public int HistoryRetentionDays { get; set; } = 30;

    /// <summary>How often the owning instance bumps <c>ScheduleRun.LastHeartbeatAt</c> while a run is in flight.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A <c>Claimed</c>/<c>Running</c> row whose heartbeat is older than this is considered orphaned
    /// and marked <c>Failed</c> by the sweep. Keep comfortably larger than <c>HeartbeatInterval</c>.
    /// </summary>
    public TimeSpan OrphanTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Bounded catch-up policy. 0 = drop missed fires (default, safest). &gt;0 = queue up to this many
    /// missed fires per schedule after a downtime.
    /// </summary>
    public int MaxCatchUp { get; set; }

    /// <summary>Identifier reported on claimed <c>ScheduleRun</c> rows. Defaults to machine name.</summary>
    public string InstanceId { get; set; } = Environment.MachineName;
}
