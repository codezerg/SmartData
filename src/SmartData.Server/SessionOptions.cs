namespace SmartData.Server;

public class SessionOptions
{
    /// <summary>
    /// Session time-to-live. With sliding expiration, sessions expire after this duration of inactivity.
    /// With absolute expiration, sessions expire this long after creation.
    /// </summary>
    public TimeSpan SessionTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// When true, TTL resets on each activity. When false, sessions expire at creation time + TTL.
    /// </summary>
    public bool SlidingExpiration { get; set; } = true;

    /// <summary>
    /// How often the cleanup service scans for and removes expired sessions, in seconds.
    /// </summary>
    public int CleanupIntervalSeconds { get; set; } = 60;
}
