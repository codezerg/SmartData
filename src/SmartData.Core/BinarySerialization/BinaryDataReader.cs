namespace SmartData.Core.BinarySerialization;

/// <summary>
/// Streaming row-by-row reader for binary-serialized tabular data.
/// Reads data written by BinarySerializer.WriteDataReader() or WriteDataTable().
/// Supports both bounded (fixed count) and unbounded (BeginArray/End) arrays.
/// </summary>
public sealed class BinaryDataReader : IDisposable
{
    private readonly BinarySerializationReader _reader;
    private readonly Stream _stream;
    private readonly SerializerOptions _options;
    private int _remaining; // -1 = unbounded, 0 = done, >0 = bounded remaining
    private bool _initialized;
    private bool _ended;

    /// <summary>
    /// Creates a new BinaryDataReader over the given stream.
    /// Non-seekable streams (e.g. zip entries) are automatically wrapped.
    /// </summary>
    public BinaryDataReader(Stream stream, SerializerOptions? options = null)
    {
        _stream = stream.CanSeek ? stream : new ReadAheadStream(stream);
        _options = options ?? SerializerOptions.Default;
        _reader = new BinarySerializationReader(_stream, leaveOpen: true);
    }

    /// <summary>
    /// Returns true if there may be more rows to read.
    /// </summary>
    public bool HasMore => !_ended;

    /// <summary>
    /// Reads the next row. Returns null when no more rows are available.
    /// </summary>
    public Dictionary<string, object?>? Read()
    {
        if (_ended) return null;

        if (!_initialized)
        {
            _remaining = _reader.ReadArrayHeader();
            _initialized = true;
        }

        // Check if done
        if (_remaining == -1)
        {
            // Unbounded: check for End marker
            if (_reader.IsEnd())
            {
                _reader.ReadEnd();
                _ended = true;
                return null;
            }
        }
        else if (_remaining == 0)
        {
            _ended = true;
            return null;
        }

        // Read one row (a map)
        var fieldCount = _reader.ReadMapHeader();
        var row = new Dictionary<string, object?>(fieldCount);

        for (int i = 0; i < fieldCount; i++)
        {
            var key = _reader.ReadKey();

            if (_reader.PeekType() == SerializedType.Nil)
            {
                _reader.Skip();
                row[key] = null;
            }
            else
            {
                row[key] = BinarySerializer.ReadDynamicValue(_reader, _options);
            }
        }

        if (_remaining > 0) _remaining--;

        return row;
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> rows. Returns fewer if the stream ends early.
    /// </summary>
    public List<Dictionary<string, object?>> Read(int count)
    {
        var results = new List<Dictionary<string, object?>>(count);
        for (int i = 0; i < count; i++)
        {
            var row = Read();
            if (row == null) break;
            results.Add(row);
        }
        return results;
    }

    public void Dispose()
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
