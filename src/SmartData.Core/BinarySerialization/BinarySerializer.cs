using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;

namespace SmartData.Core.BinarySerialization;

/// <summary>
/// High-level API for serializing and deserializing objects using reflection.
/// </summary>
public static class BinarySerializer
{
    private static readonly ConcurrentDictionary<Type, TypeInfo> _typeCache = new();

    /// <summary>
    /// Serializes an object to a byte array.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="value">The object to serialize.</param>
    /// <param name="options">Optional serialization options.</param>
    /// <returns>The serialized byte array.</returns>
    public static byte[] Serialize<T>(T value, SerializerOptions? options = null)
    {
        using var stream = new MemoryStream();
        Serialize(stream, value, options);
        return stream.ToArray();
    }

    /// <summary>
    /// Serializes an object to a stream.
    /// </summary>
    /// <typeparam name="T">The type of object to serialize.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="value">The object to serialize.</param>
    /// <param name="options">Optional serialization options.</param>
    public static void Serialize<T>(Stream stream, T value, SerializerOptions? options = null)
    {
        options ??= SerializerOptions.Default;
        using var writer = new BinarySerializationWriter(stream, leaveOpen: true);
        WriteValue(writer, value, typeof(T), options);
        writer.Flush();
    }

    /// <summary>
    /// Deserializes an object from a byte array.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize.</typeparam>
    /// <param name="data">The byte array to deserialize.</param>
    /// <param name="options">Optional serialization options.</param>
    /// <returns>The deserialized object.</returns>
    public static T? Deserialize<T>(byte[] data, SerializerOptions? options = null)
    {
        using var stream = new MemoryStream(data);
        return Deserialize<T>(stream, options);
    }

    /// <summary>
    /// Deserializes an object from a stream.
    /// </summary>
    /// <typeparam name="T">The type of object to deserialize.</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="options">Optional serialization options.</param>
    /// <returns>The deserialized object.</returns>
    public static T? Deserialize<T>(Stream stream, SerializerOptions? options = null)
    {
        options ??= SerializerOptions.Default;
        using var reader = new BinarySerializationReader(stream, leaveOpen: true);
        return (T?)ReadValue(reader, typeof(T), options);
    }

    private static void WriteValue(BinarySerializationWriter writer, object? value, Type declaredType, SerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNil();
            return;
        }

        var type = value.GetType();

        // Primitives and built-in types
        switch (value)
        {
            case bool b:
                writer.Write(b);
                return;
            case byte b:
                writer.Write(b);
                return;
            case sbyte sb:
                writer.Write(sb);
                return;
            case short s:
                writer.Write(s);
                return;
            case ushort us:
                writer.Write(us);
                return;
            case int i:
                writer.Write(i);
                return;
            case uint ui:
                writer.Write(ui);
                return;
            case long l:
                writer.Write(l);
                return;
            case ulong ul:
                writer.Write(ul);
                return;
            case float f:
                writer.Write(f);
                return;
            case double d:
                writer.Write(d);
                return;
            case decimal dec:
                writer.Write(dec.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            case string s:
                writer.Write(s);
                return;
            case byte[] bytes:
                writer.Write(bytes);
                return;
            case DateTime dt:
                writer.Write(dt.ToBinary());
                return;
            case DateTimeOffset dto:
                writer.Write(dto.ToUnixTimeMilliseconds());
                return;
            case TimeSpan ts:
                writer.Write(ts.Ticks);
                return;
            case Guid g:
                writer.Write(g.ToByteArray());
                return;
            case DataTable dataTable:
                WriteDataTable(writer, dataTable, options);
                return;
            case DataSet dataSet:
                WriteDataSet(writer, dataSet, options);
                return;
            case IDataReader dataReader:
                WriteDataReader(writer, dataReader, options);
                return;
            case DBNull:
                writer.WriteNil();
                return;
        }

        // Enums
        if (type.IsEnum)
        {
            writer.Write(Convert.ToInt64(value));
            return;
        }

        // Nullable<T>
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
        {
            WriteValue(writer, value, underlyingType, options);
            return;
        }

        // Arrays
        if (type.IsArray)
        {
            var array = (Array)value;
            var elementType = type.GetElementType()!;
            writer.WriteArrayHeader(array.Length);
            foreach (var item in array)
            {
                WriteValue(writer, item, elementType, options);
            }
            return;
        }

        // Dictionary
        if (value is IDictionary dict)
        {
            writer.WriteMapHeader(dict.Count);
            var keyType = typeof(object);
            var valueType = typeof(object);

            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                keyType = args[0];
                valueType = args[1];
            }

            foreach (DictionaryEntry entry in dict)
            {
                WriteValue(writer, entry.Key, keyType, options);
                WriteValue(writer, entry.Value, valueType, options);
            }
            return;
        }

        // List/Collection
        if (value is IList list)
        {
            writer.WriteArrayHeader(list.Count);
            var elementType = typeof(object);

            if (type.IsGenericType)
            {
                elementType = type.GetGenericArguments()[0];
            }

            foreach (var item in list)
            {
                WriteValue(writer, item, elementType, options);
            }
            return;
        }

        // IEnumerable (fallback)
        if (value is IEnumerable enumerable && type != typeof(string))
        {
            var items = enumerable.Cast<object>().ToList();
            writer.WriteArrayHeader(items.Count);

            var elementType = typeof(object);
            if (type.IsGenericType)
            {
                var args = type.GetGenericArguments();
                if (args.Length > 0) elementType = args[0];
            }

            foreach (var item in items)
            {
                WriteValue(writer, item, elementType, options);
            }
            return;
        }

        // Complex object
        WriteObject(writer, value, type, options);
    }

    private static void WriteObject(BinarySerializationWriter writer, object value, Type type, SerializerOptions options)
    {
        var typeInfo = GetTypeInfo(type, options);
        var members = typeInfo.Members;

        writer.WriteMapHeader(members.Count);

        foreach (var member in members)
        {
            if (options.UseKeyInterning)
            {
                writer.WriteKey(member.Name);
            }
            else
            {
                writer.Write(member.Name);
            }

            var memberValue = member.GetValue(value);
            WriteValue(writer, memberValue, member.MemberType, options);
        }
    }

    private static object? ReadValue(BinarySerializationReader reader, Type targetType, SerializerOptions options, int depth = 0)
    {
        if (depth > reader.Limits.MaxDepth)
            throw new InvalidOperationException($"Maximum nesting depth ({reader.Limits.MaxDepth}) exceeded.");

        var valueType = reader.PeekType();

        if (valueType == SerializedType.Nil)
        {
            reader.Skip();
            return null;
        }

        // Handle nullable types
        var underlyingType = Nullable.GetUnderlyingType(targetType);
        if (underlyingType != null)
        {
            return ReadValue(reader, underlyingType, options, depth);
        }

        // Primitives
        if (targetType == typeof(bool))
            return reader.ReadBoolean();
        if (targetType == typeof(byte))
            return (byte)reader.ReadInt64();
        if (targetType == typeof(sbyte))
            return (sbyte)reader.ReadInt64();
        if (targetType == typeof(short))
            return (short)reader.ReadInt64();
        if (targetType == typeof(ushort))
            return (ushort)reader.ReadInt64();
        if (targetType == typeof(int))
            return (int)reader.ReadInt64();
        if (targetType == typeof(uint))
            return (uint)reader.ReadInt64();
        if (targetType == typeof(long))
            return reader.ReadInt64();
        if (targetType == typeof(ulong))
            return reader.ReadUInt64();
        if (targetType == typeof(float))
            return reader.ReadSingle();
        if (targetType == typeof(double))
            return reader.ReadDouble();
        if (targetType == typeof(decimal))
            return decimal.Parse(reader.ReadString()!, System.Globalization.CultureInfo.InvariantCulture);
        if (targetType == typeof(string))
            return reader.ReadString();
        if (targetType == typeof(byte[]))
            return reader.ReadBinary();
        if (targetType == typeof(DateTime))
            return DateTime.FromBinary(reader.ReadInt64());
        if (targetType == typeof(DateTimeOffset))
            return DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
        if (targetType == typeof(TimeSpan))
            return TimeSpan.FromTicks(reader.ReadInt64());
        if (targetType == typeof(Guid))
            return new Guid(reader.ReadBinary());
        if (targetType == typeof(DataTable))
            return ReadDataTable(reader, options);
        if (targetType == typeof(DataSet))
            return ReadDataSet(reader, options);

        // Dynamic object - read based on serialized type
        if (targetType == typeof(object))
            return ReadDynamicValue(reader, options, depth);

        // Enums
        if (targetType.IsEnum)
        {
            return Enum.ToObject(targetType, reader.ReadInt64());
        }

        // Arrays
        if (targetType.IsArray)
        {
            return ReadArray(reader, targetType, options, depth);
        }

        // Dictionary
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            return ReadDictionary(reader, targetType, options, depth);
        }

        // List
        if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
        {
            return ReadList(reader, targetType, options, depth);
        }

        // Complex object
        if (valueType == SerializedType.Map)
        {
            return ReadObject(reader, targetType, options, depth);
        }

        throw new InvalidOperationException($"Cannot deserialize type {targetType.Name} from {valueType}");
    }

    private static Array ReadArray(BinarySerializationReader reader, Type arrayType, SerializerOptions options, int depth)
    {
        var elementType = arrayType.GetElementType()!;
        var count = reader.ReadArrayHeader();

        if (count < 0)
            throw new NotSupportedException("Unbounded arrays are not supported for deserialization");

        var array = Array.CreateInstance(elementType, count);
        for (int i = 0; i < count; i++)
        {
            array.SetValue(ReadValue(reader, elementType, options, depth + 1), i);
        }
        return array;
    }

    private static object ReadList(BinarySerializationReader reader, Type listType, SerializerOptions options, int depth)
    {
        var elementType = listType.GetGenericArguments()[0];
        var count = reader.ReadArrayHeader();

        if (count < 0)
            throw new NotSupportedException("Unbounded arrays are not supported for deserialization");

        var list = (IList)Activator.CreateInstance(listType)!;
        for (int i = 0; i < count; i++)
        {
            list.Add(ReadValue(reader, elementType, options, depth + 1));
        }
        return list;
    }

    private static object ReadDictionary(BinarySerializationReader reader, Type dictType, SerializerOptions options, int depth)
    {
        var args = dictType.GetGenericArguments();
        var keyType = args[0];
        var valueType = args[1];
        var count = reader.ReadMapHeader();

        if (count < 0)
            throw new NotSupportedException("Unbounded maps are not supported for deserialization");

        var dict = (IDictionary)Activator.CreateInstance(dictType)!;
        for (int i = 0; i < count; i++)
        {
            // Use ReadKey() for string keys to support key interning (SetKey/UseKey)
            var key = keyType == typeof(string) ? reader.ReadKey() : ReadValue(reader, keyType, options, depth + 1);
            var value = ReadValue(reader, valueType, options, depth + 1);
            dict.Add(key!, value);
        }
        return dict;
    }

    private static object ReadObject(BinarySerializationReader reader, Type type, SerializerOptions options, int depth)
    {
        var typeInfo = GetTypeInfo(type, options);
        var obj = Activator.CreateInstance(type)!;
        var count = reader.ReadMapHeader();

        if (count < 0)
            throw new NotSupportedException("Unbounded maps are not supported for deserialization");

        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadKey();
            var member = typeInfo.MembersByName.GetValueOrDefault(key);

            if (member != null && member.CanWrite)
            {
                // Check if the serialized type is compatible before reading
                if (IsTypeCompatible(reader.PeekType(), member.MemberType))
                {
                    var value = ReadValue(reader, member.MemberType, options, depth + 1);
                    member.SetValue(obj, value);
                }
                else
                {
                    // Type mismatch - skip this value and leave property at default
                    reader.Skip();
                }
            }
            else
            {
                // Property not found or not writable - skip
                reader.Skip();
            }
        }

        return obj;
    }

    private static void WriteDataTable(BinarySerializationWriter writer, DataTable table, SerializerOptions options)
    {
        // Serialize as array of dictionaries: [{col: val, ...}, ...]
        writer.WriteArrayHeader(table.Rows.Count);
        foreach (DataRow row in table.Rows)
        {
            writer.WriteMapHeader(table.Columns.Count);
            for (int i = 0; i < table.Columns.Count; i++)
            {
                var col = table.Columns[i];
                if (options.UseKeyInterning)
                    writer.WriteKey(col.ColumnName);
                else
                    writer.Write(col.ColumnName);

                var value = row[i];
                if (value == DBNull.Value)
                    writer.WriteNil();
                else
                    WriteValue(writer, value, col.DataType, options);
            }
        }
    }

    private static void WriteDataSet(BinarySerializationWriter writer, DataSet dataSet, SerializerOptions options)
    {
        // Serialize as array of DataTables: [DataTable, DataTable, ...]
        writer.WriteArrayHeader(dataSet.Tables.Count);
        foreach (DataTable table in dataSet.Tables)
        {
            WriteDataTable(writer, table, options);
        }
    }

    private static void WriteDataReader(BinarySerializationWriter writer, IDataReader reader, SerializerOptions options)
    {
        // Get column info
        var fieldCount = reader.FieldCount;
        var columnNames = new string[fieldCount];
        for (int i = 0; i < fieldCount; i++)
        {
            columnNames[i] = reader.GetName(i);
        }

        // Use streaming array since row count is unknown
        writer.BeginArray();

        while (reader.Read())
        {
            writer.WriteMapHeader(fieldCount);
            for (int i = 0; i < fieldCount; i++)
            {
                if (options.UseKeyInterning)
                    writer.WriteKey(columnNames[i]);
                else
                    writer.Write(columnNames[i]);

                if (reader.IsDBNull(i))
                    writer.WriteNil();
                else
                    WriteValue(writer, reader.GetValue(i), typeof(object), options);
            }
        }

        writer.WriteEnd();
    }

    private static DataTable ReadDataTable(BinarySerializationReader reader, SerializerOptions options)
    {
        var table = new DataTable();
        var rowCount = reader.ReadArrayHeader();
        var isUnbounded = rowCount < 0;

        int r = 0;
        while (isUnbounded ? !reader.IsEnd() : r < rowCount)
        {
            var colCount = reader.ReadMapHeader();
            var rowValues = new Dictionary<string, object?>();

            for (int c = 0; c < colCount; c++)
            {
                var colName = reader.ReadKey();

                // Add column if it doesn't exist
                if (!table.Columns.Contains(colName))
                    table.Columns.Add(colName, typeof(object));

                if (reader.PeekType() == SerializedType.Nil)
                {
                    reader.Skip();
                    rowValues[colName] = DBNull.Value;
                }
                else
                {
                    rowValues[colName] = ReadDynamicValue(reader, options);
                }
            }

            // Add row with values in column order
            var row = table.NewRow();
            foreach (var kvp in rowValues)
            {
                row[kvp.Key] = kvp.Value ?? DBNull.Value;
            }
            table.Rows.Add(row);
            r++;
        }

        if (isUnbounded)
            reader.ReadEnd();

        return table;
    }

    private static DataSet ReadDataSet(BinarySerializationReader reader, SerializerOptions options)
    {
        var dataSet = new DataSet();
        var tableCount = reader.ReadArrayHeader();

        for (int t = 0; t < tableCount; t++)
        {
            dataSet.Tables.Add(ReadDataTable(reader, options));
        }

        return dataSet;
    }

    internal static object? ReadDynamicValue(BinarySerializationReader reader, SerializerOptions options, int depth = 0)
    {
        if (depth > reader.Limits.MaxDepth)
            throw new InvalidOperationException($"Maximum nesting depth ({reader.Limits.MaxDepth}) exceeded.");

        var valueType = reader.PeekType();

        return valueType switch
        {
            SerializedType.Nil => ReadNil(reader),
            SerializedType.Boolean => reader.ReadBoolean(),
            SerializedType.Integer => reader.ReadInt64(),
            SerializedType.Float => reader.ReadDouble(),
            SerializedType.String => reader.ReadString(),
            SerializedType.Binary => reader.ReadBinary(),
            SerializedType.Array => ReadDynamicArray(reader, options, depth),
            SerializedType.Map => ReadDynamicMap(reader, options, depth),
            _ => throw new InvalidOperationException($"Unexpected serialized type: {valueType}")
        };
    }

    internal static object? ReadNil(BinarySerializationReader reader)
    {
        reader.Skip(); // Consume the Nil marker
        return null;
    }

    internal static List<object?> ReadDynamicArray(BinarySerializationReader reader, SerializerOptions options, int depth)
    {
        var count = reader.ReadArrayHeader();
        var list = new List<object?>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(ReadDynamicValue(reader, options, depth + 1));
        }
        return list;
    }

    internal static Dictionary<string, object?> ReadDynamicMap(BinarySerializationReader reader, SerializerOptions options, int depth)
    {
        var count = reader.ReadMapHeader();
        var dict = new Dictionary<string, object?>(count);
        for (int i = 0; i < count; i++)
        {
            var key = reader.ReadKey();
            var value = ReadDynamicValue(reader, options, depth + 1);
            dict[key] = value;
        }
        return dict;
    }

    private static bool IsTypeCompatible(SerializedType serializedType, Type targetType)
    {
        // Handle nullable types
        var underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null)
            targetType = underlying;

        // Nil is compatible with nullable types and reference types
        if (serializedType == SerializedType.Nil)
            return !targetType.IsValueType || underlying != null;

        return serializedType switch
        {
            SerializedType.Boolean => targetType == typeof(bool),
            SerializedType.Integer => IsNumericType(targetType) || targetType.IsEnum,
            SerializedType.Float => targetType == typeof(float) || targetType == typeof(double) || targetType == typeof(decimal),
            SerializedType.String => targetType == typeof(string) || targetType == typeof(decimal)
                || targetType == typeof(DateTime) || targetType == typeof(DateTimeOffset) || targetType == typeof(TimeSpan),
            SerializedType.Binary => targetType == typeof(byte[]) || targetType == typeof(Guid),
            SerializedType.Array => targetType.IsArray || IsListType(targetType),
            SerializedType.Map => IsDictionaryType(targetType) || IsComplexType(targetType),
            _ => true // Be permissive for unknown types
        };
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan);
    }

    private static bool IsListType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
    }

    private static bool IsDictionaryType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
    }

    private static bool IsComplexType(Type type)
    {
        // Classes (excluding string and arrays) and structs (value types that are not primitives)
        if (type.IsClass && type != typeof(string) && !type.IsArray)
            return true;

        // Structs (value types that are not primitives or enums)
        if (type.IsValueType && !type.IsPrimitive && !type.IsEnum
            && type != typeof(decimal) && type != typeof(DateTime)
            && type != typeof(DateTimeOffset) && type != typeof(TimeSpan)
            && type != typeof(Guid))
            return true;

        return false;
    }

    private static TypeInfo GetTypeInfo(Type type, SerializerOptions options)
    {
        return _typeCache.GetOrAdd(type, t => new TypeInfo(t, options));
    }

    private sealed class TypeInfo
    {
        public List<MemberInfo> Members { get; }
        public Dictionary<string, MemberInfo> MembersByName { get; }

        public TypeInfo(Type type, SerializerOptions options)
        {
            var attr = type.GetCustomAttribute<BinarySerializableAttribute>();
            var includeFields = attr?.IncludeFields ?? options.IncludeFields;

            var members = new List<MemberInfo>();

            // Get properties
            // Note: We only require CanRead for serialization. CanWrite is only needed for deserialization.
            // This allows anonymous types (which have read-only properties) to be serialized properly.
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanRead) continue;
                if (prop.GetCustomAttribute<BinaryIgnoreAttribute>() != null) continue;
                if (prop.GetIndexParameters().Length > 0) continue;

                var propAttr = prop.GetCustomAttribute<BinaryPropertyAttribute>();
                var name = propAttr?.Name ?? prop.Name;
                var order = propAttr?.Order ?? 0;

                members.Add(new MemberInfo(name, prop.PropertyType, order, prop.CanWrite,
                    obj => prop.GetValue(obj),
                    prop.CanWrite ? (obj, val) => prop.SetValue(obj, val) : null));
            }

            // Get fields
            if (includeFields)
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (field.IsInitOnly) continue;
                    if (field.GetCustomAttribute<BinaryIgnoreAttribute>() != null) continue;

                    var fieldAttr = field.GetCustomAttribute<BinaryPropertyAttribute>();
                    var name = fieldAttr?.Name ?? field.Name;
                    var order = fieldAttr?.Order ?? 0;

                    members.Add(new MemberInfo(name, field.FieldType, order, canWrite: true,
                        obj => field.GetValue(obj),
                        (obj, val) => field.SetValue(obj, val)));
                }
            }

            // Sort by order, then by name
            members.Sort((a, b) =>
            {
                var orderCompare = a.Order.CompareTo(b.Order);
                return orderCompare != 0 ? orderCompare : string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            Members = members;
            MembersByName = members.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class MemberInfo
    {
        public string Name { get; }
        public Type MemberType { get; }
        public int Order { get; }
        public bool CanWrite { get; }
        private readonly Func<object, object?> _getter;
        private readonly Action<object, object?>? _setter;

        public MemberInfo(string name, Type memberType, int order, bool canWrite,
            Func<object, object?> getter, Action<object, object?>? setter)
        {
            Name = name;
            MemberType = memberType;
            Order = order;
            CanWrite = canWrite;
            _getter = getter;
            _setter = setter;
        }

        public object? GetValue(object obj) => _getter(obj);
        public void SetValue(object obj, object? value)
        {
            if (_setter != null)
                _setter(obj, value);
        }
    }
}

/// <summary>
/// Options for the high-level serializer.
/// </summary>
public sealed class SerializerOptions
{
    /// <summary>
    /// Gets or sets whether to use key interning for property names.
    /// Default is true.
    /// </summary>
    public bool UseKeyInterning { get; init; } = true;

    /// <summary>
    /// Gets or sets whether to include public fields (not just properties).
    /// Default is false.
    /// </summary>
    public bool IncludeFields { get; init; }

    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static SerializerOptions Default { get; } = new();
}
