using System.Reflection;
using LinqToDB;
using LinqToDB.Mapping;

namespace SmartData.Server;

/// <summary>
/// Auto-generates LINQ2DB mapping schemas from entity classes.
/// </summary>
internal static class EntityMapping<T> where T : class
{
    private static MappingSchema? _mappingSchema;
    private static string? _tableName;
    private static readonly object _lock = new();

    public static string GetTableName()
    {
        if (_tableName != null)
            return _tableName;

        var tableAttr = typeof(T).GetCustomAttribute<TableAttribute>(true);
        _tableName = tableAttr?.Name ?? typeof(T).Name;
        return _tableName;
    }

    public static MappingSchema GetMappingSchema()
    {
        if (_mappingSchema != null)
            return _mappingSchema;

        lock (_lock)
        {
            if (_mappingSchema != null)
                return _mappingSchema;

            _mappingSchema = new MappingSchema();
            var builder = new FluentMappingBuilder(_mappingSchema);
            var entityBuilder = builder.Entity<T>();

            var tableName = GetTableName();
            if (!string.IsNullOrEmpty(tableName))
                entityBuilder.HasTableName(tableName);

            var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite);

            foreach (var property in properties)
                ProcessProperty(entityBuilder, property);

            builder.Build();
        }

        return _mappingSchema;
    }

    private static void ProcessProperty(EntityMappingBuilder<T> entityBuilder, PropertyInfo property)
    {
        var hasColumnAttribute = property.GetCustomAttribute<ColumnAttribute>() != null;
        var hasNotColumnAttribute = property.GetCustomAttribute<NotColumnAttribute>() != null;

        if (hasColumnAttribute || hasNotColumnAttribute)
            return;

        if (!property.CanWrite)
        {
            entityBuilder.HasAttribute(property, new NotColumnAttribute());
            return;
        }

        var propertyType = property.PropertyType;
        if (propertyType != typeof(string) &&
            (propertyType.IsArray ||
             (propertyType.IsGenericType && IsCollectionType(propertyType.GetGenericTypeDefinition()))))
        {
            entityBuilder.HasAttribute(property, new NotColumnAttribute());
            return;
        }

        var columnAttribute = new ColumnAttribute { Name = property.Name };

        columnAttribute.CanBeNull = !propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) != null;

        if (propertyType.IsEnum || Nullable.GetUnderlyingType(propertyType)?.IsEnum == true)
            columnAttribute.DataType = LinqToDB.DataType.Int32;

        entityBuilder.HasAttribute(property, columnAttribute);
    }

    internal static void ResetForTesting()
    {
        lock (_lock)
        {
            _mappingSchema = null;
            _tableName = null;
        }
    }

    private static bool IsCollectionType(Type type) =>
        type == typeof(List<>) || type == typeof(IList<>) ||
        type == typeof(ICollection<>) || type == typeof(IEnumerable<>) ||
        type == typeof(HashSet<>) || type == typeof(Dictionary<,>);
}
