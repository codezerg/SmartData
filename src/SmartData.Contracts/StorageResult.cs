namespace SmartData.Contracts;

public class StorageResult
{
    public List<StorageDatabaseItem> Databases { get; set; } = [];
    public List<StorageBackupItem> Backups { get; set; } = [];
    public long DbSize { get; set; }
    public long BackupSize { get; set; }
    public long TotalSize { get; set; }
}

public class StorageDatabaseResult
{
    public string Database { get; set; } = "";
    public long Size { get; set; }
}

public class StorageDatabaseItem
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
}

public class StorageBackupItem
{
    public string BackupId { get; set; } = "";
    public long Size { get; set; }
    public List<string> Databases { get; set; } = [];
}
