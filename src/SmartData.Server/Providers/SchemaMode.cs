namespace SmartData.Server.Providers;

/// <summary>
/// Controls whether AutoRepo automatically migrates database schema.
/// </summary>
public enum SchemaMode
{
    /// <summary>
    /// SmartData manages schema. AutoRepo compares entities to the database
    /// on first use — creates tables, adds missing columns, alters column types.
    /// </summary>
    Auto,

    /// <summary>
    /// You manage schema. No automatic migration. Entity classes must match
    /// the database. System procedures (SpTableCreate, etc.) still work for
    /// explicit operations.
    /// </summary>
    Manual
}
