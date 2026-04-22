using System.Reflection;
using LinqToDB.Mapping;

namespace SmartData.Server;

/// <summary>
/// Resolves the identity (auto-increment) property for an entity type.
/// The result is computed once per <typeparamref name="T"/> and cached.
/// </summary>
internal static class IdentityProperty<T> where T : class
{
    /// <summary>
    /// The property marked with both <see cref="PrimaryKeyAttribute"/> and
    /// <see cref="IdentityAttribute"/>, or <c>null</c> if none exists.
    /// </summary>
    public static PropertyInfo? Info { get; } = typeof(T).GetProperties()
        .Where(p => p.GetCustomAttributes<PrimaryKeyAttribute>(true).Any())
        .FirstOrDefault(p => p.GetCustomAttributes<IdentityAttribute>(true).Any());

    /// <summary>Whether the entity type has an identity property.</summary>
    public static bool Exists => Info != null;

    /// <summary>
    /// Sets the identity value on the given entity, converting to the property's type.
    /// </summary>
    public static void Set(T entity, object value) =>
        Info!.SetValue(entity, Convert.ChangeType(value, Info.PropertyType));

    /// <summary>
    /// Gets the identity value from the given entity.
    /// </summary>
    public static object? Get(T entity) => Info!.GetValue(entity);
}
