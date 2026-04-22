namespace SmartData.Server.Backup;

public class BackupHistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string Operation { get; set; } = "";
    public string BackupId { get; set; } = "";
    public string? User { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public List<string> Databases { get; set; } = [];
    public long? Size { get; set; }
    public long DurationMs { get; set; }
}
