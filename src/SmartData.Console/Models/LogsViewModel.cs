using SmartData.Contracts;

namespace SmartData.Console.Models;

public class LogsViewModel
{
    public List<LogEntry> Logs { get; set; } = [];
    public string? FilterType { get; set; }
    public string? FilterProcedure { get; set; }
    public string? Search { get; set; }
}
