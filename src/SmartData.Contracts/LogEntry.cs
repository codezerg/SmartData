namespace SmartData.Contracts;

public class LogEntry
{
    public string Type { get; set; } = "";
    public string ProcedureName { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
