using SmartData.Contracts;

namespace SmartData.Console.Models;

public class MetricsViewModel
{
    public MetricsResult Metrics { get; set; } = new();
    public string ActiveTab { get; set; } = "overview";
    public string? FilterName { get; set; }
    public string? FilterSource { get; set; }
}
