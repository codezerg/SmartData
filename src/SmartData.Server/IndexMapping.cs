using System.Reflection;
using LinqToDB.Mapping;
using SmartData.Server.Attributes;
using SmartData.Server.Providers;

namespace SmartData.Server;

/// <summary>
/// Reads [Index] and [FullTextIndex] attributes from entity classes and produces
/// IndexDefinition records for schema migration. Mirrors EntityMapping&lt;T&gt; pattern.
/// </summary>
internal static class IndexMapping<T> where T : class
{
    private static List<IndexDefinition>? _definitions;
    private static readonly object _lock = new();

    public static List<IndexDefinition> GetIndexDefinitions()
    {
        if (_definitions != null)
            return _definitions;

        lock (_lock)
        {
            if (_definitions != null)
                return _definitions;

            _definitions = BuildDefinitions();
        }

        return _definitions;
    }

    public static FullTextIndexAttribute? GetFullTextIndex()
    {
        return typeof(T).GetCustomAttribute<FullTextIndexAttribute>();
    }

    private static List<IndexDefinition> BuildDefinitions()
    {
        var type = typeof(T);
        var tableName = EntityMapping<T>.GetTableName();
        var columnProperties = GetColumnNames(type);
        var definitions = new List<IndexDefinition>();

        // Regular indexes
        var indexAttrs = type.GetCustomAttributes<IndexAttribute>();
        foreach (var attr in indexAttrs)
        {
            ValidateColumns(attr.Columns, columnProperties, type.Name, attr.Name);
            definitions.Add(new IndexDefinition(attr.Name, attr.Columns, attr.Unique));
        }

        // Full-text index
        var ftsAttr = type.GetCustomAttribute<FullTextIndexAttribute>();
        if (ftsAttr != null)
        {
            ValidateFtsColumns(ftsAttr.Columns, type);
            definitions.Add(new IndexDefinition(
                $"FTX_{tableName}",
                ftsAttr.Columns,
                Unique: false,
                IsFullText: true));
        }

        return definitions;
    }

    private static HashSet<string> GetColumnNames(Type type)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetCustomAttribute<NotColumnAttribute>() != null)
                continue;
            if (prop.GetCustomAttribute<ColumnAttribute>() != null)
                names.Add(prop.Name);
        }
        return names;
    }

    private static void ValidateColumns(string[] columns, HashSet<string> validColumns, string typeName, string indexName)
    {
        foreach (var col in columns)
        {
            if (!validColumns.Contains(col))
                throw new InvalidOperationException(
                    $"Index '{indexName}' on '{typeName}' references column '{col}' which is not a [Column]-annotated property.");
        }
    }

    private static void ValidateFtsColumns(string[] columns, Type type)
    {
        foreach (var col in columns)
        {
            var prop = type.GetProperty(col, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null)
                throw new InvalidOperationException(
                    $"[FullTextIndex] on '{type.Name}' references '{col}' which does not exist.");

            if (prop.GetCustomAttribute<ColumnAttribute>() == null)
                throw new InvalidOperationException(
                    $"[FullTextIndex] on '{type.Name}' references '{col}' which is not a [Column]-annotated property.");

            var propType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            if (propType != typeof(string))
                throw new InvalidOperationException(
                    $"[FullTextIndex] on '{type.Name}' references '{col}' which is not a string property. FTS columns must be strings.");
        }
    }

    internal static void ResetForTesting()
    {
        lock (_lock)
        {
            _definitions = null;
        }
    }
}
