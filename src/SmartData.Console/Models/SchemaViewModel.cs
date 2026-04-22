using SmartData.Contracts;

namespace SmartData.Console.Models;

public class SchemaViewModel
{
    public string Db { get; set; } = "";
    public string Table { get; set; } = "";
    public List<ColumnDetail> Columns { get; set; } = [];
    public List<IndexDetail> Indexes { get; set; } = [];
}
