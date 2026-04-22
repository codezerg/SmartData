namespace SmartData.Contracts;

public class DataImportResult
{
    public string Table { get; set; } = "";
    public int Inserted { get; set; }
    public int Replaced { get; set; }
    public int Skipped { get; set; }
    public int Deleted { get; set; }
}

public class DataImportPreview
{
    public string Table { get; set; } = "";
    public int Rows { get; set; }
    public List<string> Columns { get; set; } = [];
    public bool DryRun { get; set; } = true;
}
