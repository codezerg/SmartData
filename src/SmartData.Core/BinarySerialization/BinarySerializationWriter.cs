using System.Buffers.Binary;
using System.Text;

namespace SmartData.Core.BinarySerialization;

/// <summary>
/// Writes data in the binary serialization format.
/// </summary>
public sealed class BinarySerializationWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly Dictionary<string, int> _keyTable = new();
    private readonly Dictionary<int, string[]> _structTable = new();
    private int _nextKeyId;
    private int _nextStructId;

    /// <summary>
    /// Creates a new writer that writes to the specified stream.
    /// </summary>
    public BinarySerializationWriter(Stream stream, bool leaveOpen = false)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Writes a null value.
    /// </summary>
    public void WriteNil()
    {
        _stream.WriteByte(TypeMarkers.Nil);
    }

    /// <summary>
    /// Writes a boolean value.
    /// </summary>
    public void Write(bool value)
    {
        _stream.WriteByte(value ? TypeMarkers.True : TypeMarkers.False);
    }

    /// <summary>
    /// Writes a signed byte.
    /// </summary>
    public void Write(sbyte value)
    {
        if (value >= 0)
        {
            _stream.WriteByte((byte)value);
        }
        else if (value >= -16)
        {
            _stream.WriteByte((byte)(0xE0 | (value + 16)));
        }
        else
        {
            _stream.WriteByte(TypeMarkers.Int8);
            _stream.WriteByte((byte)value);
        }
    }

    /// <summary>
    /// Writes a byte.
    /// </summary>
    public void Write(byte value)
    {
        if (value <= 127)
        {
            _stream.WriteByte(value);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.UInt8);
            _stream.WriteByte(value);
        }
    }

    /// <summary>
    /// Writes a 16-bit signed integer.
    /// </summary>
    public void Write(short value)
    {
        if (value >= 0 && value <= 127)
        {
            _stream.WriteByte((byte)value);
        }
        else if (value >= -16 && value < 0)
        {
            _stream.WriteByte((byte)(0xE0 | (value + 16)));
        }
        else if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Int8);
            _stream.WriteByte((byte)value);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.Int16);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buffer, value);
            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// Writes a 16-bit unsigned integer.
    /// </summary>
    public void Write(ushort value)
    {
        if (value <= 127)
        {
            _stream.WriteByte((byte)value);
        }
        else if (value <= byte.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.UInt8);
            _stream.WriteByte((byte)value);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.UInt16);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// Writes a 32-bit signed integer.
    /// </summary>
    public void Write(int value)
    {
        if (value >= 0 && value <= 127)
        {
            _stream.WriteByte((byte)value);
        }
        else if (value >= -16 && value < 0)
        {
            _stream.WriteByte((byte)(0xE0 | (value + 16)));
        }
        else if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Int8);
            _stream.WriteByte((byte)value);
        }
        else if (value >= short.MinValue && value <= short.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Int16);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buffer, (short)value);
            _stream.Write(buffer);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.Int32);
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer.
    /// </summary>
    public void Write(uint value)
    {
        if (value <= 127)
        {
            _stream.WriteByte((byte)value);
        }
        else if (value <= byte.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.UInt8);
            _stream.WriteByte((byte)value);
        }
        else if (value <= ushort.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.UInt16);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
            _stream.Write(buffer);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.UInt32);
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// Writes a 64-bit signed integer.
    /// </summary>
    public void Write(long value)
    {
        if (value >= 0 && value <= 127)
        {
            _stream.WriteByte((byte)value);
        }
        else if (value >= -16 && value < 0)
        {
            _stream.WriteByte((byte)(0xE0 | (value + 16)));
        }
        else if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Int8);
            _stream.WriteByte((byte)value);
        }
        else if (value >= short.MinValue && value <= short.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Int16);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buffer, (short)value);
            _stream.Write(buffer);
        }
        else if (value >= int.MinValue && value <= int.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Int32);
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, (int)value);
            _stream.Write(buffer);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.Int64);
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// Writes a 64-bit unsigned integer.
    /// </summary>
    public void Write(ulong value)
    {
        if (value <= 127)
        {
            _stream.WriteByte((byte)value);
        }
        else if (value <= byte.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.UInt8);
            _stream.WriteByte((byte)value);
        }
        else if (value <= ushort.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.UInt16);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)value);
            _stream.Write(buffer);
        }
        else if (value <= uint.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.UInt32);
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)value);
            _stream.Write(buffer);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.UInt64);
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// Writes a 32-bit floating point number.
    /// </summary>
    public void Write(float value)
    {
        _stream.WriteByte(TypeMarkers.Float32);
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        _stream.Write(buffer);
    }

    /// <summary>
    /// Writes a 64-bit floating point number.
    /// </summary>
    public void Write(double value)
    {
        _stream.WriteByte(TypeMarkers.Float64);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        _stream.Write(buffer);
    }

    /// <summary>
    /// Writes a string.
    /// </summary>
    public void Write(string? value)
    {
        if (value is null)
        {
            WriteNil();
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        WriteStringBytes(bytes);
    }

    private void WriteStringBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length <= 31)
        {
            _stream.WriteByte((byte)(TypeMarkers.FixstrMin | bytes.Length));
        }
        else if (bytes.Length <= byte.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Str8);
            _stream.WriteByte((byte)bytes.Length);
        }
        else if (bytes.Length <= ushort.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Str16);
            Span<byte> lenBuf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lenBuf, (ushort)bytes.Length);
            _stream.Write(lenBuf);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.Str32);
            Span<byte> lenBuf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(lenBuf, (uint)bytes.Length);
            _stream.Write(lenBuf);
        }

        _stream.Write(bytes);
    }

    /// <summary>
    /// Writes binary data.
    /// </summary>
    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length <= byte.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Bin8);
            _stream.WriteByte((byte)data.Length);
        }
        else if (data.Length <= ushort.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Bin16);
            Span<byte> lenBuf = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(lenBuf, (ushort)data.Length);
            _stream.Write(lenBuf);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.Bin32);
            Span<byte> lenBuf = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(lenBuf, (uint)data.Length);
            _stream.Write(lenBuf);
        }

        _stream.Write(data);
    }

    /// <summary>
    /// Writes binary data.
    /// </summary>
    public void Write(byte[] data) => Write(data.AsSpan());

    /// <summary>
    /// Writes a fixed-size array header.
    /// </summary>
    public void WriteArrayHeader(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count <= 15)
        {
            _stream.WriteByte((byte)(TypeMarkers.FixarrayMin | count));
        }
        else if (count <= ushort.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Array16);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)count);
            _stream.Write(buffer);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.Array32);
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)count);
            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// Writes a fixed-size map header.
    /// </summary>
    public void WriteMapHeader(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (count <= 15)
        {
            _stream.WriteByte((byte)(TypeMarkers.FixmapMin | count));
        }
        else if (count <= ushort.MaxValue)
        {
            _stream.WriteByte(TypeMarkers.Map16);
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)count);
            _stream.Write(buffer);
        }
        else
        {
            _stream.WriteByte(TypeMarkers.Map32);
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)count);
            _stream.Write(buffer);
        }
    }

    /// <summary>
    /// Begins an unbounded array. Call WriteEnd() when finished.
    /// </summary>
    public void BeginArray()
    {
        _stream.WriteByte(TypeMarkers.BeginArray);
    }

    /// <summary>
    /// Begins an unbounded map. Call WriteEnd() when finished.
    /// </summary>
    public void BeginMap()
    {
        _stream.WriteByte(TypeMarkers.BeginMap);
    }

    /// <summary>
    /// Ends an unbounded array or map.
    /// </summary>
    public void WriteEnd()
    {
        _stream.WriteByte(TypeMarkers.End);
    }

    /// <summary>
    /// Defines a key for interning. Returns the key ID.
    /// </summary>
    public int SetKey(string key)
    {
        if (_keyTable.TryGetValue(key, out var existingId))
            return existingId;

        var id = _nextKeyId++;
        _keyTable[key] = id;

        _stream.WriteByte(TypeMarkers.SetKey);
        WriteVarint(id);
        Write(key);

        return id;
    }

    /// <summary>
    /// Writes a reference to a previously defined key.
    /// </summary>
    public void UseKey(int keyId)
    {
        _stream.WriteByte(TypeMarkers.UseKey);
        WriteVarint(keyId);
    }

    /// <summary>
    /// Writes a key, using interning if already defined.
    /// </summary>
    public void WriteKey(string key)
    {
        if (_keyTable.TryGetValue(key, out var id))
        {
            UseKey(id);
        }
        else
        {
            SetKey(key);
        }
    }

    /// <summary>
    /// Defines a struct template. Returns the struct ID.
    /// </summary>
    public int DefineStruct(params string[] keys)
    {
        if (keys.Length > 255)
            throw new ArgumentException("Struct cannot have more than 255 fields", nameof(keys));

        var id = _nextStructId++;
        _structTable[id] = keys;

        _stream.WriteByte(TypeMarkers.DefineStruct);
        WriteVarint(id);
        _stream.WriteByte((byte)keys.Length);

        foreach (var key in keys)
        {
            WriteKey(key);
        }

        return id;
    }

    /// <summary>
    /// Begins writing a struct instance. Values must follow in order.
    /// </summary>
    public void UseStruct(int structId)
    {
        _stream.WriteByte(TypeMarkers.UseStruct);
        WriteVarint(structId);
    }

    /// <summary>
    /// Clears the key table.
    /// </summary>
    public void ClearKeys()
    {
        _stream.WriteByte(TypeMarkers.ClearKeys);
        _keyTable.Clear();
        _nextKeyId = 0;
    }

    /// <summary>
    /// Clears the struct table.
    /// </summary>
    public void ClearStructs()
    {
        _stream.WriteByte(TypeMarkers.ClearStructs);
        _structTable.Clear();
        _nextStructId = 0;
    }

    /// <summary>
    /// Clears all tables.
    /// </summary>
    public void ClearAll()
    {
        _stream.WriteByte(TypeMarkers.ClearAll);
        _keyTable.Clear();
        _structTable.Clear();
        _nextKeyId = 0;
        _nextStructId = 0;
    }

    private void WriteVarint(int value)
    {
        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        if (value < 128)
        {
            _stream.WriteByte((byte)value);
        }
        else if (value < 16384)
        {
            _stream.WriteByte((byte)(0x80 | (value >> 8)));
            _stream.WriteByte((byte)(value & 0xFF));
        }
        else if (value < 2097152)
        {
            _stream.WriteByte((byte)(0xC0 | (value >> 16)));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
            _stream.WriteByte((byte)(value & 0xFF));
        }
        else
        {
            _stream.WriteByte((byte)(0xE0 | (value >> 24)));
            _stream.WriteByte((byte)((value >> 16) & 0xFF));
            _stream.WriteByte((byte)((value >> 8) & 0xFF));
            _stream.WriteByte((byte)(value & 0xFF));
        }
    }

    /// <summary>
    /// Flushes the underlying stream.
    /// </summary>
    public void Flush() => _stream.Flush();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }
}
