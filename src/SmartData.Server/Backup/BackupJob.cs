namespace SmartData.Server.Backup;

internal class BackupJob
{
    public string JobId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string? BackupId { get; set; }
    public List<string> Databases { get; set; } = [];
    public bool Force { get; set; }
    public string? User { get; set; }

    // Mutable state (updated by runner)
    public string Status { get; set; } = "pending";
    public double Progress { get; set; }
    public string? ProgressMessage { get; set; }
    public long? Size { get; set; }
    public long ElapsedMs { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; }
    public CancellationTokenSource Cts { get; set; } = new();
}
