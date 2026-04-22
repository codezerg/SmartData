using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SmartData.Server.Metrics;

/// <summary>
/// Singleton registry for all metrics instruments, spans, and exceptions.
/// Thread-safe — called from concurrent request handlers.
/// </summary>
internal sealed class MetricsCollector
{
    private readonly ConcurrentDictionary<string, Counter> _counters = new();
    private readonly ConcurrentDictionary<string, Histogram> _histograms = new();
    private readonly ConcurrentDictionary<string, Gauge> _gauges = new();
    private readonly RingBuffer<Span> _completedSpans;
    private readonly RingBuffer<ExceptionRecord> _exceptions;
    private readonly MetricsOptions _options;
    private readonly ILogger<MetricsCollector> _logger;
    private readonly bool _enabled;

    // Self-observability (internal — not subject to cardinality limits)
    private long _droppedCount;
    private long _cardinalityDroppedCount;

    /// <summary>
    /// Event fired when span/exception buffer reaches capacity threshold.
    /// MetricsFlushService subscribes to trigger early flush.
    /// </summary>
    internal event Action? CapacityThresholdReached;

    public MetricsCollector(IOptions<MetricsOptions> options, ILogger<MetricsCollector> logger)
    {
        _options = options.Value;
        _logger = logger;
        _enabled = _options.Enabled;
        _completedSpans = new RingBuffer<Span>(_options.SpanBufferCapacity);
        _exceptions = new RingBuffer<ExceptionRecord>(_options.ExceptionBufferCapacity);
    }

    public bool Enabled => _enabled;

    // --- Instruments ---

    public Counter Counter(string name)
    {
        if (!_enabled) return GetNoOpCounter(name);
        return _counters.GetOrAdd(name, n =>
        {
            var counter = new Counter(n, _options.MaxSeriesPerInstrument);
            counter.CardinalityExceeded += OnCardinalityExceeded;
            return counter;
        });
    }

    public Histogram Histogram(string name)
    {
        if (!_enabled) return GetNoOpHistogram(name);
        return _histograms.GetOrAdd(name, n =>
        {
            var histogram = new Histogram(n, _options.MaxSeriesPerInstrument);
            histogram.CardinalityExceeded += OnCardinalityExceeded;
            return histogram;
        });
    }

    public Gauge Gauge(string name)
    {
        if (!_enabled) return GetNoOpGauge(name);
        return _gauges.GetOrAdd(name, n =>
        {
            var gauge = new Gauge(n, _options.MaxSeriesPerInstrument);
            gauge.CardinalityExceeded += OnCardinalityExceeded;
            return gauge;
        });
    }

    // --- Tracing ---

    public ISpan StartSpan(string name, params (string Key, string Value)[] tags)
    {
        if (!_enabled) return NoOpSpan.Instance;

        // If parent is a NoOpSpan scenario (parent is null due to NoOp), check sampling
        var parent = Span.Current;

        // Sampling: if no parent (root span), apply sample rate
        if (parent == null && _options.TraceSampleRate < 1.0)
        {
            if (Random.Shared.NextDouble() > _options.TraceSampleRate)
                return NoOpSpan.Instance;
        }

        var tagSet = tags.Length == 0 ? TagSet.Empty : new TagSet(tags);
        return new Span(name, tagSet, this);
    }

    internal void OnSpanCompleted(Span span)
    {
        _completedSpans.Add(span);

        if (_completedSpans.FillRatio >= _options.FlushOnCapacityRatio)
            CapacityThresholdReached?.Invoke();
    }

    // --- Exceptions ---

    public void TrackException(Exception ex, params (string Key, string Value)[] tags)
    {
        if (!_enabled) return;

        var currentSpan = Span.Current;
        var properties = new Dictionary<string, string>();
        foreach (var (key, value) in tags)
            properties[key] = value;

        var record = new ExceptionRecord
        {
            ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
            Message = ex.Message,
            StackTrace = ex.StackTrace ?? "",
            Procedure = properties.GetValueOrDefault("procedure"),
            Database = properties.GetValueOrDefault("db"),
            User = properties.GetValueOrDefault("user"),
            TraceId = currentSpan?.TraceId,
            SpanId = currentSpan?.SpanId,
            Properties = properties
        };

        _exceptions.Add(record);

        if (_exceptions.FillRatio >= _options.FlushOnCapacityRatio)
            CapacityThresholdReached?.Invoke();
    }

    // --- Snapshots ---

    public MetricSnapshot GetSnapshot()
    {
        return new MetricSnapshot
        {
            Counters = _counters.Values
                .SelectMany(c => c.Snapshot().Select(s => new CounterSnapshot(c.Name, s.Tags, s.Value)))
                .ToList(),
            Histograms = _histograms.Values
                .SelectMany(h => h.Snapshot().Select(s => new HistogramDataSnapshot(h.Name, s.Tags, s.Count, s.Sum, s.Min, s.Max, s.P50, s.P95, s.P99)))
                .ToList(),
            Gauges = _gauges.Values
                .SelectMany(g => g.Snapshot().Select(s => new GaugeSnapshot(g.Name, s.Tags, s.Value)))
                .ToList(),
            Spans = _completedSpans.ToList(),
            Exceptions = _exceptions.ToList()
        };
    }

    public FlushData CollectAndReset()
    {
        return new FlushData
        {
            Counters = _counters.Values
                .SelectMany(c => c.CollectAndReset().Select(s => new CounterSnapshot(c.Name, s.Tags, s.Value)))
                .ToList(),
            Histograms = _histograms.Values
                .SelectMany(h => h.CollectAndReset().Select(s => new HistogramDataSnapshot(h.Name, s.Tags, s.Count, s.Sum, s.Min, s.Max, s.P50, s.P95, s.P99)))
                .ToList(),
            Gauges = _gauges.Values
                .SelectMany(g => g.Snapshot().Select(s => new GaugeSnapshot(g.Name, s.Tags, s.Value)))
                .ToList(),
            Spans = _completedSpans.Drain(),
            Exceptions = _exceptions.Drain()
        };
    }

    // --- Self-observability ---

    internal (long Dropped, long CardinalityDropped, double SpanBufferUsage, double ExceptionBufferUsage) GetSelfMetrics()
    {
        return (
            Interlocked.Read(ref _droppedCount),
            Interlocked.Read(ref _cardinalityDroppedCount),
            _completedSpans.FillRatio,
            _exceptions.FillRatio
        );
    }

    private void OnCardinalityExceeded(string instrumentName)
    {
        Interlocked.Increment(ref _cardinalityDroppedCount);
        Interlocked.Increment(ref _droppedCount);
        _logger.LogWarning("Metrics cardinality limit ({Limit}) exceeded for instrument '{Instrument}'. New tag combinations will be dropped.",
            _options.MaxSeriesPerInstrument, instrumentName);
    }

    // --- No-op instruments (returned when disabled) ---

    private static readonly ConcurrentDictionary<string, Counter> _noOpCounters = new();
    private static readonly ConcurrentDictionary<string, Histogram> _noOpHistograms = new();
    private static readonly ConcurrentDictionary<string, Gauge> _noOpGauges = new();

    private static Counter GetNoOpCounter(string name) => _noOpCounters.GetOrAdd(name, n => new Counter(n, 0));
    private static Histogram GetNoOpHistogram(string name) => _noOpHistograms.GetOrAdd(name, n => new Histogram(n, 0));
    private static Gauge GetNoOpGauge(string name) => _noOpGauges.GetOrAdd(name, n => new Gauge(n, 0));
}
