namespace SmartData.Contracts;

public class DatabaseListItem
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
}
