using System;

namespace SmartData.Core.BinarySerialization;

/// <summary>
/// Marks a property or field for binary serialization with optional configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class BinaryPropertyAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the serialized name. If null, the member name is used.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the serialization order. Lower values are serialized first.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Creates a new instance with default settings.
    /// </summary>
    public BinaryPropertyAttribute() { }

    /// <summary>
    /// Creates a new instance with the specified name.
    /// </summary>
    /// <param name="name">The serialized property name.</param>
    public BinaryPropertyAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Excludes a property or field from binary serialization.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class BinaryIgnoreAttribute : Attribute
{
}

/// <summary>
/// Configures binary serialization options for a class or struct.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class BinarySerializableAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether to use key interning for property names.
    /// Default is true.
    /// </summary>
    public bool UseKeyInterning { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to include fields (not just properties).
    /// Default is false.
    /// </summary>
    public bool IncludeFields { get; set; }
}
