using System.Collections.Concurrent;

namespace SmartData.Server.Metrics;

/// <summary>
/// Point-in-time gauge. Represents a current value (e.g., active connections, queue depth).
/// </summary>
internal sealed class Gauge
{
    private readonly ConcurrentDictionary<TagSet, long> _values = new();
    private readonly int _maxSeries;
    private volatile bool _cardinalityWarned;

    public string Name { get; }

    public Gauge(string name, int maxSeries)
    {
        Name = name;
        _maxSeries = maxSeries;
    }

    public void Set(double value, params (string Key, string Value)[] tags)
    {
        var tagSet = tags.Length == 0 ? TagSet.Empty : new TagSet(tags);
        SetInternal(tagSet, value);
    }

    private void SetInternal(TagSet tagSet, double value)
    {
        if (!_values.ContainsKey(tagSet) && _values.Count >= _maxSeries)
        {
            if (!_cardinalityWarned)
            {
                _cardinalityWarned = true;
                CardinalityExceeded?.Invoke(Name);
            }
            return;
        }

        _values[tagSet] = BitConverter.DoubleToInt64Bits(value);
    }

    public void Increment(params (string Key, string Value)[] tags)
    {
        var tagSet = tags.Length == 0 ? TagSet.Empty : new TagSet(tags);
        _values.AddOrUpdate(tagSet,
            BitConverter.DoubleToInt64Bits(1.0),
            (_, old) => BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(old) + 1.0));
    }

    public void Decrement(params (string Key, string Value)[] tags)
    {
        var tagSet = tags.Length == 0 ? TagSet.Empty : new TagSet(tags);
        _values.AddOrUpdate(tagSet,
            BitConverter.DoubleToInt64Bits(-1.0),
            (_, old) => BitConverter.DoubleToInt64Bits(BitConverter.Int64BitsToDouble(old) - 1.0));
    }

    public List<(TagSet Tags, double Value)> Snapshot()
    {
        return _values.Select(kv => (kv.Key, BitConverter.Int64BitsToDouble(kv.Value))).ToList();
    }

    internal event Action<string>? CardinalityExceeded;
}
