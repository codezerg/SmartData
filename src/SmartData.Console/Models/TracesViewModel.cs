using SmartData.Contracts;

namespace SmartData.Console.Models;

public class TracesViewModel
{
    public TracesResult Traces { get; set; } = new();
    public List<ProcedureTraceStats> ProcedureStats { get; set; } = [];
    public string ActiveTab { get; set; } = "overview";
    public string? FilterProcedure { get; set; }
    public bool ErrorsOnly { get; set; }
    public double? MinDurationMs { get; set; }
}

public class ProcedureTraceStats
{
    public string Procedure { get; set; } = "";
    public int CallCount { get; set; }
    public int ErrorCount { get; set; }
    public double AvgDurationMs { get; set; }
    public double MaxDurationMs { get; set; }
    public double P95DurationMs { get; set; }
    public DateTime LastSeen { get; set; }
}

public class TraceDetailViewModel
{
    public string TraceId { get; set; } = "";
    public List<SpanItem> Spans { get; set; } = [];
}
