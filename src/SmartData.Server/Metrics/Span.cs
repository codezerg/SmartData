using SmartData.Core;

namespace SmartData.Server.Metrics;

internal enum SpanStatus { Ok, Error }

/// <summary>
/// Common interface for spans — allows callers to use "using var span = ..." uniformly
/// whether the span is real or a no-op.
/// </summary>
internal interface ISpan : IDisposable
{
    void SetAttribute(string key, object value);
    void SetError(Exception ex);
}

/// <summary>
/// Represents a unit of work with start/end time and parent-child nesting.
/// Uses AsyncLocal for automatic parent tracking across async calls.
/// </summary>
internal sealed class Span : ISpan
{
    private static readonly AsyncLocal<Span?> _current = new();

    public static Span? Current => _current.Value;

    private readonly Span? _parent;
    private readonly MetricsCollector _collector;
    private bool _disposed;

    public string TraceId { get; }
    public string SpanId { get; }
    public string? ParentSpanId { get; }
    public string Name { get; }
    public TagSet Tags { get; }
    public Dictionary<string, string> Attributes { get; } = [];
    public DateTime StartTime { get; } = DateTime.UtcNow;
    public DateTime? EndTime { get; private set; }
    public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : TimeSpan.Zero;
    public SpanStatus Status { get; private set; } = SpanStatus.Ok;
    public string? ErrorMessage { get; private set; }
    public string? ErrorType { get; private set; }

    internal Span(string name, TagSet tags, MetricsCollector collector)
    {
        _parent = _current.Value;
        _collector = collector;

        TraceId = _parent?.TraceId ?? IdGenerator.NewId();
        SpanId = IdGenerator.NewId();
        ParentSpanId = _parent?.SpanId;
        Name = name;
        Tags = tags;

        _current.Value = this;
    }

    public void SetAttribute(string key, object value)
    {
        Attributes[key] = value?.ToString() ?? "";
    }

    public void SetError(Exception ex)
    {
        Status = SpanStatus.Error;
        ErrorMessage = ex.Message;
        ErrorType = ex.GetType().Name;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        EndTime = DateTime.UtcNow;
        _current.Value = _parent;
        _collector.OnSpanCompleted(this);
    }
}

/// <summary>
/// Lightweight no-op span returned when sampling is off or metrics disabled.
/// Does not allocate tracking state or touch AsyncLocal.
/// Children of a NoOpSpan also get NoOpSpan (sampling decision propagates down).
/// </summary>
internal sealed class NoOpSpan : ISpan
{
    public static readonly NoOpSpan Instance = new();

    public void SetAttribute(string key, object value) { }
    public void SetError(Exception ex) { }
    public void Dispose() { }
}
