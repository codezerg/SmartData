using SmartData.Contracts;

namespace SmartData.Console.Models;

public class ImportViewModel
{
    public string Db { get; set; } = "";
    public string Table { get; set; } = "";
}

public class ImportPreviewViewModel
{
    public string Db { get; set; } = "";
    public string Table { get; set; } = "";
    public string Json { get; set; } = "";
    public int RowCount { get; set; }
    public List<string> FileColumns { get; set; } = [];
    public List<ColumnDetail> TableColumns { get; set; } = [];
    public HashSet<string> TableColumnNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class ImportResultViewModel
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
}
