using SmartData.Server;
using SmartData.Server.Backup;
using SmartData.Server.Metrics;
using SmartData.Server.Providers;
using SmartData.Server.Scheduling;

namespace SmartData;

public class IndexOptions
{
    /// <summary>
    /// Prefix added to all attribute-declared index names in the database.
    /// Only indexes with this prefix are eligible for auto-drop when removed from code.
    /// </summary>
    public string Prefix { get; set; } = "SD_";

    /// <summary>
    /// Auto-drop indexes that were previously declared via [Index] attributes
    /// but have since been removed from the entity class.
    /// </summary>
    public bool AutoDrop { get; set; } = true;

    /// <summary>
    /// Auto-create indexes declared via [Index] attributes during schema migration.
    /// </summary>
    public bool AutoCreate { get; set; } = true;

    /// <summary>
    /// Auto-create full-text search indexes declared via [FullTextIndex] attributes.
    /// </summary>
    public bool AutoCreateFullText { get; set; } = true;
}

public class SmartDataOptions
{
    public SchemaMode SchemaMode { get; set; } = SchemaMode.Auto;

    /// <summary>
    /// When true, error responses include exception details (ex.Message).
    /// When false, clients receive a generic error message and details are logged server-side only.
    /// Defaults to true in Development, false otherwise.
    /// </summary>
    public bool IncludeExceptionDetails { get; set; }

    public IndexOptions Index { get; set; } = new();
    public MetricsOptions Metrics { get; set; } = new();
    public BackupOptions Backup { get; set; } = new();
    public SessionOptions Session { get; set; } = new();
    public SchedulerOptions Scheduler { get; set; } = new();
}
