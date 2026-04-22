namespace SmartData.Contracts;

public class BackupListItem
{
    public string BackupId { get; set; } = "";
    public List<string> Databases { get; set; } = [];
    public long Size { get; set; }
    public DateTime Created { get; set; }
}
