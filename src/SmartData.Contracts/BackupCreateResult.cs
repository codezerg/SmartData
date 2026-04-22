namespace SmartData.Contracts;

public class BackupCreateResult
{
    public string JobId { get; set; } = "";
    public string BackupId { get; set; } = "";
    public List<string> Databases { get; set; } = [];
    public long Size { get; set; }
    public long DurationMs { get; set; }
}
