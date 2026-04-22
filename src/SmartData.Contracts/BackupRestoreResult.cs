namespace SmartData.Contracts;

public class BackupRestoreResult
{
    public string JobId { get; set; } = "";
    public string Message { get; set; } = "";
    public List<string> Databases { get; set; } = [];
    public long DurationMs { get; set; }
}
