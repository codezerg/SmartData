namespace SmartData.Server.SqliteEncrypted;

public class RekeyResult
{
    public bool Success { get; set; }
    public string DbName { get; set; } = string.Empty;
}
