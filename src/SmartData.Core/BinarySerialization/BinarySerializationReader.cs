using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SmartData.Core.BinarySerialization;

/// <summary>
/// Reads data in the binary serialization format.
/// </summary>
public sealed class BinarySerializationReader : IDisposable
{
    private readonly Stream _stream;
    private readonly bool _leaveOpen;
    private readonly Dictionary<int, string> _keyTable = new();
    private readonly Dictionary<int, string[]> _structTable = new();
    private readonly ReaderLimits _limits;

    /// <summary>
    /// Gets the limits configured for this reader.
    /// </summary>
    public ReaderLimits Limits => _limits;

    /// <summary>
    /// Creates a new reader that reads from the specified stream.
    /// </summary>
    public BinarySerializationReader(Stream stream, bool leaveOpen = false, ReaderLimits? limits = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _leaveOpen = leaveOpen;
        _limits = limits ?? ReaderLimits.Default;
    }

    /// <summary>
    /// Peeks at the next type marker without consuming it.
    /// </summary>
    public SerializedType PeekType()
    {
        var marker = _stream.ReadByte();
        if (marker == -1)
            return SerializedType.EndOfStream;

        // Push back by seeking if possible, otherwise we need a different approach
        if (_stream.CanSeek)
        {
            _stream.Seek(-1, SeekOrigin.Current);
        }
        else
        {
            throw new NotSupportedException("PeekType requires a seekable stream");
        }

        return GetSerializedType((byte)marker);
    }

    /// <summary>
    /// Reads the next value and returns its type.
    /// </summary>
    public SerializedType ReadType()
    {
        var marker = _stream.ReadByte();
        if (marker == -1)
            return SerializedType.EndOfStream;

        if (_stream.CanSeek)
        {
            _stream.Seek(-1, SeekOrigin.Current);
        }
        else
        {
            throw new NotSupportedException("ReadType requires a seekable stream for push-back");
        }

        return GetSerializedType((byte)marker);
    }

    private static SerializedType GetSerializedType(byte marker)
    {
        return marker switch
        {
            <= TypeMarkers.PositiveFixintMax => SerializedType.Integer,
            >= TypeMarkers.FixmapMin and <= TypeMarkers.FixmapMax => SerializedType.Map,
            >= TypeMarkers.FixarrayMin and <= TypeMarkers.FixarrayMax => SerializedType.Array,
            >= TypeMarkers.FixstrMin and <= TypeMarkers.FixstrMax => SerializedType.String,
            TypeMarkers.Nil => SerializedType.Nil,
            TypeMarkers.False or TypeMarkers.True => SerializedType.Boolean,
            TypeMarkers.Bin8 or TypeMarkers.Bin16 or TypeMarkers.Bin32 => SerializedType.Binary,
            TypeMarkers.Float32 or TypeMarkers.Float64 => SerializedType.Float,
            >= TypeMarkers.UInt8 and <= TypeMarkers.Int64 => SerializedType.Integer,
            TypeMarkers.Str8 or TypeMarkers.Str16 or TypeMarkers.Str32 => SerializedType.String,
            TypeMarkers.Array16 or TypeMarkers.Array32 => SerializedType.Array,
            TypeMarkers.Map16 or TypeMarkers.Map32 => SerializedType.Map,
            >= TypeMarkers.NegativeFixintMin and <= TypeMarkers.NegativeFixintMax => SerializedType.Integer,
            TypeMarkers.SetKey => SerializedType.Command,
            TypeMarkers.UseKey => SerializedType.Key,
            TypeMarkers.DefineStruct => SerializedType.Command,
            TypeMarkers.UseStruct => SerializedType.Struct,
            TypeMarkers.ClearKeys or TypeMarkers.ClearStructs or TypeMarkers.ClearAll => SerializedType.Command,
            TypeMarkers.BeginArray => SerializedType.Array,
            TypeMarkers.BeginMap => SerializedType.Map,
            TypeMarkers.End => SerializedType.End,
            _ => SerializedType.Unknown
        };
    }

    /// <summary>
    /// Reads a boolean value.
    /// </summary>
    public bool ReadBoolean()
    {
        var marker = ReadByte();
        return marker switch
        {
            TypeMarkers.True => true,
            TypeMarkers.False => false,
            _ => throw new InvalidDataException($"Expected boolean, got 0x{marker:X2}")
        };
    }

    /// <summary>
    /// Reads a signed 32-bit integer.
    /// </summary>
    public int ReadInt32()
    {
        var marker = ReadByte();

        if (marker <= TypeMarkers.PositiveFixintMax)
            return marker;

        if (marker >= TypeMarkers.NegativeFixintMin && marker <= TypeMarkers.NegativeFixintMax)
            return (marker & 0x0F) - 16;

        return marker switch
        {
            TypeMarkers.Int8 => (sbyte)ReadByte(),
            TypeMarkers.Int16 => ReadInt16BigEndian(),
            TypeMarkers.Int32 => ReadInt32BigEndian(),
            TypeMarkers.UInt8 => ReadByte(),
            TypeMarkers.UInt16 => ReadUInt16BigEndian(),
            _ => throw new InvalidDataException($"Expected integer, got 0x{marker:X2}")
        };
    }

    /// <summary>
    /// Reads a signed 64-bit integer.
    /// </summary>
    public long ReadInt64()
    {
        var marker = ReadByte();

        if (marker <= TypeMarkers.PositiveFixintMax)
            return marker;

        if (marker >= TypeMarkers.NegativeFixintMin && marker <= TypeMarkers.NegativeFixintMax)
            return (marker & 0x0F) - 16;

        return marker switch
        {
            TypeMarkers.Int8 => (sbyte)ReadByte(),
            TypeMarkers.Int16 => ReadInt16BigEndian(),
            TypeMarkers.Int32 => ReadInt32BigEndian(),
            TypeMarkers.Int64 => ReadInt64BigEndian(),
            TypeMarkers.UInt8 => ReadByte(),
            TypeMarkers.UInt16 => ReadUInt16BigEndian(),
            TypeMarkers.UInt32 => ReadUInt32BigEndian(),
            _ => throw new InvalidDataException($"Expected integer, got 0x{marker:X2}")
        };
    }

    /// <summary>
    /// Reads an unsigned 64-bit integer.
    /// </summary>
    public ulong ReadUInt64()
    {
        var marker = ReadByte();

        if (marker <= TypeMarkers.PositiveFixintMax)
            return marker;

        return marker switch
        {
            TypeMarkers.UInt8 => ReadByte(),
            TypeMarkers.UInt16 => ReadUInt16BigEndian(),
            TypeMarkers.UInt32 => ReadUInt32BigEndian(),
            TypeMarkers.UInt64 => ReadUInt64BigEndian(),
            _ => throw new InvalidDataException($"Expected unsigned integer, got 0x{marker:X2}")
        };
    }

    /// <summary>
    /// Reads a 32-bit floating point number.
    /// </summary>
    public float ReadSingle()
    {
        var marker = ReadByte();
        if (marker != TypeMarkers.Float32)
            throw new InvalidDataException($"Expected float32, got 0x{marker:X2}");

        Span<byte> buffer = stackalloc byte[4];
        ReadExact(buffer);
        return BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    /// <summary>
    /// Reads a 64-bit floating point number.
    /// </summary>
    public double ReadDouble()
    {
        var marker = ReadByte();
        if (marker == TypeMarkers.Float32)
        {
            // Accept Float32 and widen to double
            Span<byte> buf4 = stackalloc byte[4];
            ReadExact(buf4);
            return BinaryPrimitives.ReadSingleBigEndian(buf4);
        }
        if (marker != TypeMarkers.Float64)
            throw new InvalidDataException($"Expected float64, got 0x{marker:X2}");

        Span<byte> buffer = stackalloc byte[8];
        ReadExact(buffer);
        return BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }

    /// <summary>
    /// Reads a string.
    /// </summary>
    public string? ReadString()
    {
        var marker = ReadByte();

        if (marker == TypeMarkers.Nil)
            return null;

        int length;
        if (marker >= TypeMarkers.FixstrMin && marker <= TypeMarkers.FixstrMax)
        {
            length = marker & 0x1F;
        }
        else if (marker == TypeMarkers.Str8)
        {
            length = ReadByte();
        }
        else if (marker == TypeMarkers.Str16)
        {
            length = ReadUInt16BigEndian();
        }
        else if (marker == TypeMarkers.Str32)
        {
            length = (int)ReadUInt32BigEndian();
        }
        else
        {
            throw new InvalidDataException($"Expected string, got 0x{marker:X2}");
        }

        if (length > _limits.MaxStringLength)
            throw new InvalidDataException($"String length {length} exceeds limit {_limits.MaxStringLength}");

        if (length == 0)
            return string.Empty;

        var buffer = new byte[length];
        ReadExact(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>
    /// Reads binary data.
    /// </summary>
    public byte[] ReadBinary()
    {
        var marker = ReadByte();

        int length = marker switch
        {
            TypeMarkers.Bin8 => ReadByte(),
            TypeMarkers.Bin16 => ReadUInt16BigEndian(),
            TypeMarkers.Bin32 => (int)ReadUInt32BigEndian(),
            _ => throw new InvalidDataException($"Expected binary, got 0x{marker:X2}")
        };

        if (length > _limits.MaxBinaryLength)
            throw new InvalidDataException($"Binary length {length} exceeds limit {_limits.MaxBinaryLength}");

        var buffer = new byte[length];
        ReadExact(buffer);
        return buffer;
    }

    /// <summary>
    /// Reads an array header and returns the element count.
    /// Returns -1 for unbounded arrays (BEGIN_ARRAY).
    /// </summary>
    public int ReadArrayHeader()
    {
        var marker = ReadByte();

        if (marker >= TypeMarkers.FixarrayMin && marker <= TypeMarkers.FixarrayMax)
            return marker & 0x0F;

        return marker switch
        {
            TypeMarkers.Array16 => ReadUInt16BigEndian(),
            TypeMarkers.Array32 => (int)ReadUInt32BigEndian(),
            TypeMarkers.BeginArray => -1,
            _ => throw new InvalidDataException($"Expected array, got 0x{marker:X2}")
        };
    }

    /// <summary>
    /// Reads a map header and returns the pair count.
    /// Returns -1 for unbounded maps (BEGIN_MAP).
    /// </summary>
    public int ReadMapHeader()
    {
        var marker = ReadByte();

        if (marker >= TypeMarkers.FixmapMin && marker <= TypeMarkers.FixmapMax)
            return marker & 0x0F;

        return marker switch
        {
            TypeMarkers.Map16 => ReadUInt16BigEndian(),
            TypeMarkers.Map32 => (int)ReadUInt32BigEndian(),
            TypeMarkers.BeginMap => -1,
            _ => throw new InvalidDataException($"Expected map, got 0x{marker:X2}")
        };
    }

    /// <summary>
    /// Checks if the next marker is END.
    /// </summary>
    public bool IsEnd()
    {
        var type = PeekType();
        return type == SerializedType.End;
    }

    /// <summary>
    /// Reads and discards an END marker.
    /// </summary>
    public void ReadEnd()
    {
        var marker = ReadByte();
        if (marker != TypeMarkers.End)
            throw new InvalidDataException($"Expected END, got 0x{marker:X2}");
    }

    /// <summary>
    /// Reads a key (either inline string or USE_KEY reference).
    /// </summary>
    public string ReadKey()
    {
        var position = _stream.Position;
        var marker = ReadByte();

        // Check for USE_KEY
        if (marker == TypeMarkers.UseKey)
        {
            var id = ReadVarint();
            if (!_keyTable.TryGetValue(id, out var key))
                throw new InvalidDataException($"Unknown key ID: {id} (position: {position}, keyTable count: {_keyTable.Count})");
            return key;
        }

        // Check for SET_KEY
        if (marker == TypeMarkers.SetKey)
        {
            var id = ReadVarint();
            if (_keyTable.Count >= _limits.MaxKeyTableSize)
                throw new InvalidDataException($"Key table size exceeds limit {_limits.MaxKeyTableSize}");

            // Read the string value directly (we're already positioned after the varint)
            var key = ReadStringDirect();
            _keyTable[id] = key;
            return key;
        }

        // Otherwise it should be an inline string
        if (!_stream.CanSeek)
            throw new NotSupportedException("Non-seekable stream requires USE_KEY or SET_KEY");

        _stream.Seek(-1, SeekOrigin.Current);
        var result = ReadString();
        if (result == null)
        {
            throw new InvalidDataException($"Key cannot be null (position: {position}, marker: 0x{marker:X2}, keyTable: [{string.Join(", ", _keyTable.Select(kv => $"{kv.Key}={kv.Value}"))}])");
        }
        return result;
    }

    private string ReadStringDirect()
    {
        var marker = ReadByte();

        int length;
        if (marker >= TypeMarkers.FixstrMin && marker <= TypeMarkers.FixstrMax)
        {
            length = marker & 0x1F;
        }
        else if (marker == TypeMarkers.Str8)
        {
            length = ReadByte();
        }
        else if (marker == TypeMarkers.Str16)
        {
            length = ReadUInt16BigEndian();
        }
        else if (marker == TypeMarkers.Str32)
        {
            length = (int)ReadUInt32BigEndian();
        }
        else
        {
            throw new InvalidDataException($"Expected string, got 0x{marker:X2}");
        }

        if (length > _limits.MaxStringLength)
            throw new InvalidDataException($"String length {length} exceeds limit {_limits.MaxStringLength}");

        if (length == 0)
            return string.Empty;

        var buffer = new byte[length];
        ReadExact(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>
    /// Reads a struct header (USE_STRUCT) and returns the field names.
    /// </summary>
    public string[] ReadStructHeader()
    {
        var marker = ReadByte();

        // Handle DEFINE_STRUCT
        if (marker == TypeMarkers.DefineStruct)
        {
            var id = ReadVarint();
            if (_structTable.Count >= _limits.MaxStructTableSize)
                throw new InvalidDataException($"Struct table size exceeds limit {_limits.MaxStructTableSize}");

            var fieldCount = ReadByte();
            var keys = new string[fieldCount];
            for (int i = 0; i < fieldCount; i++)
            {
                keys[i] = ReadKey();
            }

            _structTable[id] = keys;
            return keys;
        }

        // Handle USE_STRUCT
        if (marker == TypeMarkers.UseStruct)
        {
            var id = ReadVarint();
            if (!_structTable.TryGetValue(id, out var keys))
                throw new InvalidDataException($"Unknown struct ID: {id}");
            return keys;
        }

        throw new InvalidDataException($"Expected struct, got 0x{marker:X2}");
    }

    /// <summary>
    /// Processes a command marker (ClearKeys, ClearStructs, ClearAll).
    /// </summary>
    public void ProcessCommand()
    {
        var marker = ReadByte();

        switch (marker)
        {
            case TypeMarkers.ClearKeys:
                _keyTable.Clear();
                break;
            case TypeMarkers.ClearStructs:
                _structTable.Clear();
                break;
            case TypeMarkers.ClearAll:
                _keyTable.Clear();
                _structTable.Clear();
                break;
            default:
                throw new InvalidDataException($"Expected command, got 0x{marker:X2}");
        }
    }

    /// <summary>
    /// Skips the next value.
    /// </summary>
    public void Skip()
    {
        var marker = ReadByte();

        // Positive fixint
        if (marker <= TypeMarkers.PositiveFixintMax)
            return;

        // Fixmap
        if (marker >= TypeMarkers.FixmapMin && marker <= TypeMarkers.FixmapMax)
        {
            var count = marker & 0x0F;
            for (int i = 0; i < count * 2; i++)
                Skip();
            return;
        }

        // Fixarray
        if (marker >= TypeMarkers.FixarrayMin && marker <= TypeMarkers.FixarrayMax)
        {
            var count = marker & 0x0F;
            for (int i = 0; i < count; i++)
                Skip();
            return;
        }

        // Fixstr
        if (marker >= TypeMarkers.FixstrMin && marker <= TypeMarkers.FixstrMax)
        {
            var length = marker & 0x1F;
            SkipBytes(length);
            return;
        }

        // Negative fixint
        if (marker >= TypeMarkers.NegativeFixintMin && marker <= TypeMarkers.NegativeFixintMax)
            return;

        switch (marker)
        {
            case TypeMarkers.Nil:
            case TypeMarkers.False:
            case TypeMarkers.True:
                return;
            case TypeMarkers.Bin8:
            case TypeMarkers.Str8:
                SkipBytes(ReadByte());
                return;
            case TypeMarkers.Bin16:
            case TypeMarkers.Str16:
                SkipBytes(ReadUInt16BigEndian());
                return;
            case TypeMarkers.Bin32:
            case TypeMarkers.Str32:
                SkipBytes((int)ReadUInt32BigEndian());
                return;
            case TypeMarkers.Float32:
            case TypeMarkers.Int32:
            case TypeMarkers.UInt32:
                SkipBytes(4);
                return;
            case TypeMarkers.Float64:
            case TypeMarkers.Int64:
            case TypeMarkers.UInt64:
                SkipBytes(8);
                return;
            case TypeMarkers.Int8:
            case TypeMarkers.UInt8:
                SkipBytes(1);
                return;
            case TypeMarkers.Int16:
            case TypeMarkers.UInt16:
                SkipBytes(2);
                return;
            case TypeMarkers.Array16:
                var arr16Count = ReadUInt16BigEndian();
                for (int i = 0; i < arr16Count; i++)
                    Skip();
                return;
            case TypeMarkers.Array32:
                var arr32Count = (int)ReadUInt32BigEndian();
                for (int i = 0; i < arr32Count; i++)
                    Skip();
                return;
            case TypeMarkers.Map16:
                var map16Count = ReadUInt16BigEndian();
                for (int i = 0; i < map16Count * 2; i++)
                    Skip();
                return;
            case TypeMarkers.Map32:
                var map32Count = (int)ReadUInt32BigEndian();
                for (int i = 0; i < map32Count * 2; i++)
                    Skip();
                return;

            // Command markers
            case TypeMarkers.SetKey:
                ReadVarint(); // key id
                Skip();       // string value
                return;
            case TypeMarkers.UseKey:
                ReadVarint(); // key id
                return;
            case TypeMarkers.DefineStruct:
                ReadVarint();           // struct id
                var fieldCount = ReadByte(); // field count
                for (int i = 0; i < fieldCount; i++)
                    Skip(); // skip each key
                return;
            case TypeMarkers.UseStruct:
                ReadVarint(); // struct id
                return;
            case TypeMarkers.ClearKeys:
            case TypeMarkers.ClearStructs:
            case TypeMarkers.ClearAll:
            case TypeMarkers.End:
                return; // no payload
            case TypeMarkers.BeginArray:
                // Skip until END marker
                while (true)
                {
                    if (PeekType() == SerializedType.End)
                    {
                        ReadEnd();
                        break;
                    }
                    Skip();
                }
                return;
            case TypeMarkers.BeginMap:
                // Skip until END marker (key-value pairs)
                while (true)
                {
                    if (PeekType() == SerializedType.End)
                    {
                        ReadEnd();
                        break;
                    }
                    Skip(); // key
                    Skip(); // value
                }
                return;

            default:
                throw new InvalidDataException($"Cannot skip marker 0x{marker:X2}");
        }
    }

    private int ReadVarint()
    {
        var first = ReadByte();

        if (first < 0x80)
            return first;

        if (first < 0xC0)
        {
            var second = ReadByte();
            return ((first & 0x3F) << 8) | second;
        }

        if (first < 0xE0)
        {
            var second = ReadByte();
            var third = ReadByte();
            return ((first & 0x1F) << 16) | (second << 8) | third;
        }

        var b2 = ReadByte();
        var b3 = ReadByte();
        var b4 = ReadByte();
        return ((first & 0x0F) << 24) | (b2 << 16) | (b3 << 8) | b4;
    }

    private byte ReadByte()
    {
        var b = _stream.ReadByte();
        if (b == -1)
            throw new EndOfStreamException();
        return (byte)b;
    }

    private void ReadExact(Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = _stream.Read(buffer.Slice(offset));
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }

    private void SkipBytes(int count)
    {
        if (_stream.CanSeek)
        {
            _stream.Seek(count, SeekOrigin.Current);
        }
        else
        {
            var buffer = new byte[Math.Min(count, 4096)];
            while (count > 0)
            {
                var toRead = Math.Min(count, buffer.Length);
                var read = _stream.Read(buffer, 0, toRead);
                if (read == 0)
                    throw new EndOfStreamException();
                count -= read;
            }
        }
    }

    private short ReadInt16BigEndian()
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExact(buffer);
        return BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    private int ReadInt32BigEndian()
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExact(buffer);
        return BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    private long ReadInt64BigEndian()
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExact(buffer);
        return BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    private ushort ReadUInt16BigEndian()
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExact(buffer);
        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    private uint ReadUInt32BigEndian()
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExact(buffer);
        return BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    private ulong ReadUInt64BigEndian()
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExact(buffer);
        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }
}

/// <summary>
/// Value types in the binary format.
/// </summary>
public enum SerializedType
{
    /// <summary>Unknown or unrecognized type marker.</summary>
    Unknown,

    /// <summary>End of stream reached.</summary>
    EndOfStream,

    /// <summary>Null/nil value.</summary>
    Nil,

    /// <summary>Boolean value (true or false).</summary>
    Boolean,

    /// <summary>Integer value (signed or unsigned, any size).</summary>
    Integer,

    /// <summary>Floating-point value (float or double).</summary>
    Float,

    /// <summary>UTF-8 string value.</summary>
    String,

    /// <summary>Binary data (byte array).</summary>
    Binary,

    /// <summary>Array (fixed-size or unbounded).</summary>
    Array,

    /// <summary>Map/dictionary (fixed-size or unbounded).</summary>
    Map,

    /// <summary>Command marker (SET_KEY, DEFINE_STRUCT, CLEAR_*, etc.).</summary>
    Command,

    /// <summary>Key reference (USE_KEY or SET_KEY).</summary>
    Key,

    /// <summary>Struct instance (USE_STRUCT or DEFINE_STRUCT).</summary>
    Struct,

    /// <summary>End marker for unbounded collections.</summary>
    End
}

/// <summary>
/// Limits for the reader to prevent DoS attacks.
/// </summary>
public sealed class ReaderLimits
{
    /// <summary>
    /// Maximum string length in bytes.
    /// </summary>
    public int MaxStringLength { get; init; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Maximum binary data length in bytes.
    /// </summary>
    public int MaxBinaryLength { get; init; } = 100 * 1024 * 1024; // 100MB

    /// <summary>
    /// Maximum number of entries in the key table.
    /// </summary>
    public int MaxKeyTableSize { get; init; } = 10000;

    /// <summary>
    /// Maximum number of entries in the struct table.
    /// </summary>
    public int MaxStructTableSize { get; init; } = 1000;

    /// <summary>
    /// Maximum nesting depth.
    /// </summary>
    public int MaxDepth { get; init; } = 100;

    /// <summary>
    /// Default limits.
    /// </summary>
    public static ReaderLimits Default { get; } = new();
}
