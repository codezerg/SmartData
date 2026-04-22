using SmartData.Contracts;

namespace SmartData.Console.Models;

public class DatabaseViewModel
{
    public string Db { get; set; } = "";
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public List<TableListItem> Tables { get; set; } = [];
    public string ActiveTab { get; set; } = "details";
    public List<BackupListItem> Backups { get; set; } = [];
}
