namespace SmartData.Contracts;

public class BackupJobStatus
{
    public string JobId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Status { get; set; } = "";
    public string? BackupId { get; set; }
    public List<string> Databases { get; set; } = [];
    public long? Size { get; set; }
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }
    public double Progress { get; set; }
    public string? ProgressMessage { get; set; }
}
