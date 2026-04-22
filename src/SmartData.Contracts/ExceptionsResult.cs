namespace SmartData.Contracts;

public class ExceptionsResult
{
    public List<ExceptionItem> Items { get; set; } = [];
    public int Total { get; set; }
}

public class ExceptionItem
{
    public string ExceptionType { get; set; } = "";
    public string Message { get; set; } = "";
    public string StackTrace { get; set; } = "";
    public string? Procedure { get; set; }
    public string? Database { get; set; }
    public string? User { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? Properties { get; set; }
    public DateTime Timestamp { get; set; }
}
