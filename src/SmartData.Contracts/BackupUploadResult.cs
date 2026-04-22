namespace SmartData.Contracts;

public class BackupUploadResult
{
    public string BackupId { get; set; } = "";
    public long Offset { get; set; }
    public long TotalSize { get; set; }
    public bool Done { get; set; }
}
