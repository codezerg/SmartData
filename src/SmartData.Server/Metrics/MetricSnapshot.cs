namespace SmartData.Server.Metrics;

/// <summary>
/// Point-in-time snapshot of all live metrics data. Returned by MetricsCollector.GetSnapshot().
/// </summary>
internal sealed class MetricSnapshot
{
    public List<CounterSnapshot> Counters { get; init; } = [];
    public List<HistogramDataSnapshot> Histograms { get; init; } = [];
    public List<GaugeSnapshot> Gauges { get; init; } = [];
    public List<Span> Spans { get; init; } = [];
    public List<ExceptionRecord> Exceptions { get; init; } = [];
}

internal readonly record struct CounterSnapshot(string Name, TagSet Tags, long Value);
internal readonly record struct HistogramDataSnapshot(
    string Name, TagSet Tags, long Count, double Sum, double Min, double Max,
    double P50, double P95, double P99);
internal readonly record struct GaugeSnapshot(string Name, TagSet Tags, double Value);

/// <summary>
/// Data returned by CollectAndReset() for flushing to the database.
/// Counters and histograms are reset; gauges are snapshot-only.
/// </summary>
internal sealed class FlushData
{
    public List<CounterSnapshot> Counters { get; init; } = [];
    public List<HistogramDataSnapshot> Histograms { get; init; } = [];
    public List<GaugeSnapshot> Gauges { get; init; } = [];
    public List<Span> Spans { get; init; } = [];
    public List<ExceptionRecord> Exceptions { get; init; } = [];
}
