namespace SmartData.Contracts;

public class QueryResult
{
    public List<string> Columns { get; set; } = [];
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public int AffectedRows { get; set; }
}
