namespace SmartData.Server.SqlServer;

public class SqlServerDatabaseOptions
{
    /// <summary>
    /// Base connection string (Server + authentication). Do not include Initial Catalog — the provider
    /// appends it per database. Example: "Server=localhost;User Id=sa;Password=...;TrustServerCertificate=True"
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Working directory for backups and exports. Defaults to {BaseDir}/data.
    /// </summary>
    public string DataDirectory { get; set; } = string.Empty;
}
