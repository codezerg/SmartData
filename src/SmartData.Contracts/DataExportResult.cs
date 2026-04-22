namespace SmartData.Contracts;

public class DataExportResult
{
    public string Table { get; set; } = "";
    public int Count { get; set; }
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
}
