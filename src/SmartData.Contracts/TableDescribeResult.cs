namespace SmartData.Contracts;

public class TableDescribeResult
{
    public string Table { get; set; } = "";
    public List<ColumnDetail> Columns { get; set; } = [];
    public List<IndexDetail> Indexes { get; set; } = [];
}

public class ColumnDetail
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Nullable { get; set; }
    public int Pk { get; set; }
    public bool PrimaryKey => Pk > 0;
}

public class IndexDetail
{
    public string Name { get; set; } = "";
    public string? Sql { get; set; }
}
