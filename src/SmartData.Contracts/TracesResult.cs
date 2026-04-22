namespace SmartData.Contracts;

public class TracesResult
{
    public List<TraceItem> Traces { get; set; } = [];
    public List<SpanItem> Spans { get; set; } = [];
    public int Total { get; set; }
}

public class TraceItem
{
    public string TraceId { get; set; } = "";
    public string RootSpanName { get; set; } = "";
    public double TotalDurationMs { get; set; }
    public int SpanCount { get; set; }
    public bool HasErrors { get; set; }
    public DateTime StartTime { get; set; }
}

public class SpanItem
{
    public string TraceId { get; set; } = "";
    public string SpanId { get; set; } = "";
    public string? ParentSpanId { get; set; }
    public string Name { get; set; } = "";
    public string? Tags { get; set; }
    public string? Attributes { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public double DurationMs { get; set; }
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public string? ErrorType { get; set; }
}
