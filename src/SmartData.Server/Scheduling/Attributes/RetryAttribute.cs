namespace SmartData.Server.Scheduling.Attributes;

/// <summary>
/// Seeds <c>RetryAttempts</c> and <c>RetryIntervalSeconds</c> on the <c>SysSchedule</c> rows
/// for this procedure. After seeding, retry policy is user-editable via <c>sp_schedule_update</c>.
/// </summary>
/// <remarks>
/// WARNING — convention differs from common retry libraries. <paramref name="attempts"/> is the
/// <b>total</b> number of runs (initial + retries). <c>[Retry(3)]</c> means 1 initial + 2 retries.
/// <c>attempts = 1</c> is equivalent to no attribute.
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RetryAttribute : Attribute
{
    public RetryAttribute(int attempts, int intervalSeconds = 60)
    {
        if (attempts < 1) throw new ArgumentOutOfRangeException(nameof(attempts), "attempts must be >= 1.");
        if (intervalSeconds < 0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        Attempts = attempts;
        IntervalSeconds = intervalSeconds;
    }

    /// <summary>Total runs including the first. 1 = no retry.</summary>
    public int Attempts { get; }

    public int IntervalSeconds { get; }
}
