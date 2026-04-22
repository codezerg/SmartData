using SmartData.Contracts;

namespace SmartData.Console.Models;

public class DashboardViewModel
{
    public List<DatabaseListItem> Databases { get; set; } = [];
    public MetricsResult Metrics { get; set; } = new();
    public ExceptionsResult RecentExceptions { get; set; } = new();
    public StorageResult Storage { get; set; } = new();
}
