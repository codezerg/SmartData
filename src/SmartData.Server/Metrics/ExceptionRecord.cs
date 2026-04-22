namespace SmartData.Server.Metrics;

/// <summary>
/// Structured exception capture with full context.
/// </summary>
internal sealed class ExceptionRecord
{
    public required string ExceptionType { get; init; }
    public required string Message { get; init; }
    public required string StackTrace { get; init; }
    public string? Procedure { get; init; }
    public string? Database { get; init; }
    public string? User { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Dictionary<string, string> Properties { get; init; } = [];
}
