namespace SmartData.Server.Backup;

public class BackupOptions
{
    public int? MaxBackupAge { get; set; }
    public int? MaxBackupCount { get; set; }
    public int? MaxHistoryAge { get; set; }
    public int? MaxHistoryCount { get; set; }
}
