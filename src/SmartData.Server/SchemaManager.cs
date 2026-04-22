using System.ComponentModel.DataAnnotations;
using System.Reflection;
using LinqToDB.Mapping;
using SmartData.Server.Attributes;
using SmartData.Server.Providers;
using SmartData.Server.Tracking;

namespace SmartData.Server;

/// <summary>
/// Automatic schema migration. On first use per database+entity, ensures the database
/// schema matches the entity definition — creating tables, adding columns, and altering
/// column types/nullability as needed.
/// </summary>
internal static class SchemaManager<T> where T : class, new()
{
    private static readonly object _lock = new();
    private static readonly HashSet<string> _ensured = new(StringComparer.OrdinalIgnoreCase);

    public static void EnsureSchema(string dbName, IDatabaseProvider provider, IndexOptions? indexOptions = null)
    {
        var key = $"{dbName}::{EntityMapping<T>.GetTableName()}";

        if (_ensured.Contains(key))
            return;

        lock (_lock)
        {
            if (_ensured.Contains(key))
                return;

            var tableName = EntityMapping<T>.GetTableName();
            var schemaOps = provider.SchemaOperations;
            var snapshot = provider.Schema.GetTableSchema(dbName, tableName);

            if (!snapshot.Exists)
            {
                var columns = GetEntityColumns(schemaOps);
                schemaOps.CreateTable(dbName, tableName, columns);
                EnsureIndexes(dbName, tableName, [], provider, indexOptions ?? new IndexOptions());
            }
            else
            {
                var entityColumns = GetEntityColumns(schemaOps);

                foreach (var entityCol in entityColumns)
                {
                    var tableCol = snapshot.Columns.FirstOrDefault(c =>
                        string.Equals(c.Name, entityCol.Name, StringComparison.OrdinalIgnoreCase));

                    if (tableCol == null)
                    {
                        var sqlType = schemaOps.MapType(entityCol.Type, entityCol.Length);
                        schemaOps.AddColumn(dbName, tableName, entityCol.Name, sqlType, entityCol.Nullable);
                    }
                    else if (!ColumnsMatch(tableCol, entityCol, schemaOps))
                    {
                        var sqlType = schemaOps.MapType(entityCol.Type, entityCol.Length);
                        schemaOps.AlterColumn(dbName, tableName, entityCol.Name, sqlType, entityCol.Nullable, snapshot.Columns);
                    }
                }

                EnsureIndexes(dbName, tableName, snapshot.Indexes, provider, indexOptions ?? new IndexOptions());
            }

            _ensured.Add(key);
        }

        // Provision {Table}_History on the same path, after the source table is guaranteed to exist.
        TrackingSchemaManager<T>.EnsureHistorySchema(dbName, provider, indexOptions ?? new IndexOptions());
    }

    private static List<ColumnDefinition> GetEntityColumns(ISchemaOperations schemaOps)
    {
        var mappingSchema = EntityMapping<T>.GetMappingSchema();
        var descriptor = mappingSchema.GetEntityDescriptor(typeof(T));

        return descriptor.Columns.Select(c => new ColumnDefinition(
            Name: c.ColumnName,
            Type: GetLogicalType(c),
            Nullable: c.IsPrimaryKey ? false : c.CanBeNull,
            PrimaryKey: c.IsPrimaryKey,
            Identity: c.IsIdentity,
            Length: ResolveLength(c)
        )).ToList();
    }

    /// <summary>
    /// Resolves column length from multiple attribute sources (in priority order):
    /// 1. [Column(Length = N)] — linq2db
    /// 2. [MaxLength(N)] — System.ComponentModel.DataAnnotations
    /// 3. [StringLength(N)] — System.ComponentModel.DataAnnotations
    /// </summary>
    private static int? ResolveLength(ColumnDescriptor column)
    {
        if (column.Length is > 0)
            return column.Length.Value;

        var member = column.MemberInfo;

        var maxLength = member.GetCustomAttribute<MaxLengthAttribute>();
        if (maxLength?.Length is > 0)
            return maxLength.Length;

        var stringLength = member.GetCustomAttribute<StringLengthAttribute>();
        if (stringLength?.MaximumLength is > 0)
            return stringLength.MaximumLength;

        return null;
    }

    private static string GetLogicalType(ColumnDescriptor column)
    {
        var type = Nullable.GetUnderlyingType(column.MemberType) ?? column.MemberType;

        if (type == typeof(string)) return "string";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(DateTime)) return "datetime";
        if (type == typeof(Guid)) return "guid";
        if (type == typeof(byte[])) return "byte[]";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(double) || type == typeof(float)) return "double";
        if (type.IsEnum) return "int";
        return "string";
    }

    private static bool ColumnsMatch(ProviderColumnInfo dbColumn, ColumnDefinition entityColumn, ISchemaOperations schemaOps)
    {
        var entitySqlType = schemaOps.MapType(entityColumn.Type, entityColumn.Length).Trim().ToUpperInvariant();
        var dbSqlType = (dbColumn.Type ?? "").Trim().ToUpperInvariant();

        return string.Equals(entitySqlType, dbSqlType, StringComparison.OrdinalIgnoreCase) &&
               dbColumn.IsNullable == entityColumn.Nullable;
    }

    private static void EnsureIndexes(string dbName, string tableName,
        IReadOnlyList<ProviderIndexInfo> existingIndexes, IDatabaseProvider provider, IndexOptions options)
    {
        var declaredIndexes = IndexMapping<T>.GetIndexDefinitions();
        var schemaOps = provider.SchemaOperations;

        // Track prefixed index names for stale cleanup
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var declared in declaredIndexes)
        {
            if (declared.IsFullText)
            {
                if (options.AutoCreateFullText)
                    EnsureFullTextIndex(dbName, tableName, declared, schemaOps);
                continue;
            }

            if (!options.AutoCreate)
                continue;

            // Apply prefix to the developer-declared name
            var prefixedName = $"{options.Prefix}{declared.Name}";
            declaredNames.Add(prefixedName);

            var existing = existingIndexes.FirstOrDefault(i =>
                string.Equals(i.Name, prefixedName, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                var columns = string.Join(", ", declared.Columns.Select(c => $"[{c}]"));
                schemaOps.CreateIndex(dbName, tableName, prefixedName, columns, declared.Unique);
            }
            else if (!IndexMatchesDeclared(existing, declared))
            {
                schemaOps.DropIndex(dbName, prefixedName);
                var columns = string.Join(", ", declared.Columns.Select(c => $"[{c}]"));
                schemaOps.CreateIndex(dbName, tableName, prefixedName, columns, declared.Unique);
            }
        }

        // Drop stale auto-managed indexes (prefixed name no longer in attributes)
        if (options.AutoDrop)
            DropStaleIndexes(dbName, options.Prefix, declaredNames, existingIndexes, schemaOps);
    }

    private static void EnsureFullTextIndex(string dbName, string tableName, IndexDefinition declared, ISchemaOperations schemaOps)
    {
        var ftsExists = schemaOps.FullTextIndexExists(dbName, tableName);
        if (!ftsExists)
        {
            schemaOps.CreateFullTextIndex(dbName, tableName, declared.Columns);
        }
    }

    private static bool IndexMatchesDeclared(ProviderIndexInfo existing, IndexDefinition declared)
    {
        // Compare uniqueness
        if (existing.IsUnique != declared.Unique)
            return false;

        // Compare columns (normalize: strip brackets, trim, case-insensitive)
        if (existing.Columns == null)
            return false;

        var existingCols = existing.Columns
            .Replace("[", "").Replace("]", "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (existingCols.Length != declared.Columns.Length)
            return false;

        for (int i = 0; i < existingCols.Length; i++)
        {
            if (!string.Equals(existingCols[i], declared.Columns[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static void DropStaleIndexes(string dbName, string indexPrefix,
        HashSet<string> declaredNames, IReadOnlyList<ProviderIndexInfo> existingIndexes, ISchemaOperations schemaOps)
    {
        // Only auto-drop indexes matching the configured prefix (auto-managed)
        foreach (var existing in existingIndexes)
        {
            if (existing.Name.StartsWith(indexPrefix, StringComparison.OrdinalIgnoreCase) &&
                !declaredNames.Contains(existing.Name))
            {
                schemaOps.DropIndex(dbName, existing.Name);
            }
        }
    }

    internal static void ResetForTesting()
    {
        lock (_lock)
        {
            _ensured.Clear();
        }
    }
}
