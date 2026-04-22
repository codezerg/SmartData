namespace SmartData.Contracts;

public class BackupDownloadResult
{
    public byte[] Data { get; set; } = [];
    public long Offset { get; set; }
    public long TotalSize { get; set; }
    public bool Done { get; set; }
}
