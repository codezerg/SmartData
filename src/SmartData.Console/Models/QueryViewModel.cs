namespace SmartData.Console.Models;

public class QueryViewModel
{
    public string Db { get; set; } = "";
    public string Table { get; set; } = "";
    public List<string> ColumnNames { get; set; } = [];
    public Dictionary<string, string> ColumnTypes { get; set; } = [];
}

public class QueryResultViewModel
{
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public List<string> Columns { get; set; } = [];
    public string? Filter { get; set; }
    public string? Error { get; set; }
}
