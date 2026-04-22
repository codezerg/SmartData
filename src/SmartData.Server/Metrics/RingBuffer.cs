namespace SmartData.Server.Metrics;

/// <summary>
/// Thread-safe fixed-capacity ring buffer. Overwrites oldest items when full.
/// </summary>
internal sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly object _lock = new();
    private int _head;
    private int _count;

    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _buffer = new T[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count { get { lock (_lock) return _count; } }
    public double FillRatio { get { lock (_lock) return (double)_count / _buffer.Length; } }

    public void Add(T item)
    {
        lock (_lock)
        {
            _buffer[_head] = item;
            _head = (_head + 1) % _buffer.Length;
            if (_count < _buffer.Length)
                _count++;
        }
    }

    /// <summary>
    /// Returns a snapshot of all items (oldest first) without clearing.
    /// </summary>
    public List<T> ToList()
    {
        lock (_lock)
        {
            var list = new List<T>(_count);
            if (_count == 0) return list;

            var start = _count < _buffer.Length ? 0 : _head;
            for (var i = 0; i < _count; i++)
                list.Add(_buffer[(start + i) % _buffer.Length]);

            return list;
        }
    }

    /// <summary>
    /// Returns all items and resets the buffer. Used by flush service.
    /// </summary>
    public List<T> Drain()
    {
        lock (_lock)
        {
            var list = ToListInternal();
            _head = 0;
            _count = 0;
            Array.Clear(_buffer);
            return list;
        }
    }

    private List<T> ToListInternal()
    {
        var list = new List<T>(_count);
        if (_count == 0) return list;

        var start = _count < _buffer.Length ? 0 : _head;
        for (var i = 0; i < _count; i++)
            list.Add(_buffer[(start + i) % _buffer.Length]);

        return list;
    }
}
