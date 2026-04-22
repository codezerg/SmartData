namespace SmartData.Server.Metrics;

/// <summary>
/// Configuration for the metrics collection system.
/// </summary>
public class MetricsOptions
{
    /// <summary>Master on/off switch.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Fraction of requests to trace (0.0–1.0). Errors are always traced.</summary>
    public double TraceSampleRate { get; set; } = 1.0;

    /// <summary>How often to flush to DB, in seconds.</summary>
    public int FlushIntervalSeconds { get; set; } = 60;

    /// <summary>Delete DB data older than this many days.</summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>Queries slower than this (ms) get tagged slow=true.</summary>
    public int SlowQueryThresholdMs { get; set; } = 500;

    /// <summary>Ring buffer capacity for completed spans.</summary>
    public int SpanBufferCapacity { get; set; } = 1000;

    /// <summary>Ring buffer capacity for captured exceptions.</summary>
    public int ExceptionBufferCapacity { get; set; } = 500;

    /// <summary>Max unique TagSets per instrument before dropping new series.</summary>
    public int MaxSeriesPerInstrument { get; set; } = 1000;

    /// <summary>Flush spans/exceptions when buffer reaches this fill ratio (0.0–1.0).</summary>
    public double FlushOnCapacityRatio { get; set; } = 0.8;

    /// <summary>Prefix for rolling daily metric databases. Names: {prefix}_{yyyy_MM_dd}.</summary>
    public string DatabasePrefix { get; set; } = "_metrics";
}
