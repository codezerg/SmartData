namespace SmartData.Console.Models;

public class DataGridViewModel
{
    public string Db { get; set; } = "";
    public string Table { get; set; } = "";
    public List<Dictionary<string, object?>> Rows { get; set; } = [];
    public List<string> Columns { get; set; } = [];
    public int Offset { get; set; }
    public int Limit { get; set; } = 50;
    public string? OrderBy { get; set; }
    public string? Search { get; set; }
    public string ActiveTab { get; set; } = "data";
}
