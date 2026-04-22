namespace SmartData.Contracts;

public class MetricsResult
{
    public List<MetricItem> Items { get; set; } = [];
    public int Total { get; set; }
}

public class MetricItem
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Tags { get; set; }
    public double Value { get; set; }
    public long? Count { get; set; }
    public double? Sum { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public double? P50 { get; set; }
    public double? P95 { get; set; }
    public double? P99 { get; set; }
    public DateTime CreatedAt { get; set; }
}
