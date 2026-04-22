namespace SmartData.Server.Backup;

internal class BackupManifest
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public List<string> Databases { get; set; } = [];
    public Dictionary<string, string> Checksums { get; set; } = [];
}

internal class BackupSchemaDefinition
{
    public List<BackupTableDefinition> Tables { get; set; } = [];
}

internal class BackupTableDefinition
{
    public string Name { get; set; } = "";
    public List<BackupColumnDefinition> Columns { get; set; } = [];
    public List<BackupIndexDefinition> Indexes { get; set; } = [];
}

internal class BackupColumnDefinition
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Nullable { get; set; }
    public bool PrimaryKey { get; set; }
    public bool Identity { get; set; }
}

internal class BackupIndexDefinition
{
    public string Name { get; set; } = "";
    public string Columns { get; set; } = "";
    public bool Unique { get; set; }
}
