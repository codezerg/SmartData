namespace SmartData.Server.Providers;

/// <summary>
/// Table metadata returned by ISchemaProvider.
/// </summary>
public record ProviderTableInfo(string Name, int ColumnCount, int RowCount);

/// <summary>
/// Column metadata returned by ISchemaProvider.
/// </summary>
public record ProviderColumnInfo(string Name, string Type, bool IsNullable, bool IsPrimaryKey, bool IsIdentity = false);

/// <summary>
/// Index metadata returned by ISchemaProvider.
/// </summary>
public record ProviderIndexInfo(string Name, string? Sql, string? Columns = null, bool IsUnique = false);

/// <summary>
/// Column definition for creating tables via ISchemaOperations.
/// Type is a logical type (string, int, decimal, bool, datetime, guid, byte[], long, double).
/// The provider maps it to the database-specific SQL type.
/// </summary>
public record ColumnDefinition(string Name, string Type, bool Nullable, bool PrimaryKey, bool Identity = false, int? Length = null);

/// <summary>
/// Index definition declared via [Index] or [FullTextIndex] attributes on entity classes.
/// Used by SchemaManager to auto-create/update/drop indexes during schema migration.
/// </summary>
public record IndexDefinition(string Name, string[] Columns, bool Unique, bool IsFullText = false);

/// <summary>
/// Bundled schema snapshot for a single table — existence, columns, and indexes in one call.
/// Used by SchemaManager to avoid multiple connection round-trips during validation.
/// </summary>
public record TableSchemaSnapshot(
    bool Exists,
    IReadOnlyList<ProviderColumnInfo> Columns,
    IReadOnlyList<ProviderIndexInfo> Indexes);

/// <summary>
/// Database metadata returned by IDatabaseProvider.GetDatabaseInfo().
/// </summary>
public record DatabaseInfo(string Name, long Size, DateTime CreatedAt, DateTime ModifiedAt);
