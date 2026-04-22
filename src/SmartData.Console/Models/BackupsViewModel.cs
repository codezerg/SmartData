using SmartData.Contracts;
using SmartData.Server.Backup;

namespace SmartData.Console.Models;

public class BackupsViewModel
{
    public List<BackupListItem> Backups { get; set; } = [];
    public List<DatabaseListItem> Databases { get; set; } = [];
    public List<BackupHistoryEntry> History { get; set; } = [];
    public string ActiveTab { get; set; } = "backups";
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public BackupJobStatus? ActiveJob { get; set; }
}
