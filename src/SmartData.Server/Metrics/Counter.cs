using System.Collections.Concurrent;

namespace SmartData.Server.Metrics;

/// <summary>
/// Monotonically increasing counter. Lock-free via Interlocked.Add.
/// </summary>
internal sealed class Counter
{
    private ConcurrentDictionary<TagSet, long> _values = new();
    private readonly int _maxSeries;
    private volatile bool _cardinalityWarned;

    public string Name { get; }

    public Counter(string name, int maxSeries)
    {
        Name = name;
        _maxSeries = maxSeries;
    }

    public void Add(long delta, params (string Key, string Value)[] tags)
    {
        var tagSet = tags.Length == 0 ? TagSet.Empty : new TagSet(tags);
        AddInternal(tagSet, delta);
    }

    internal void Add(long delta, TagSet tagSet)
    {
        AddInternal(tagSet, delta);
    }

    private void AddInternal(TagSet tagSet, long delta)
    {
        if (_values.ContainsKey(tagSet))
        {
            _values.AddOrUpdate(tagSet, delta, (_, old) => old + delta);
            return;
        }

        if (_values.Count >= _maxSeries)
        {
            if (!_cardinalityWarned)
            {
                _cardinalityWarned = true;
                CardinalityExceeded?.Invoke(Name);
            }
            return;
        }

        _values.AddOrUpdate(tagSet, delta, (_, old) => old + delta);
    }

    public List<(TagSet Tags, long Value)> Snapshot()
    {
        return _values.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Atomically swaps the values dictionary and returns the old one for flushing.
    /// </summary>
    public List<(TagSet Tags, long Value)> CollectAndReset()
    {
        var old = Interlocked.Exchange(ref _values, new ConcurrentDictionary<TagSet, long>());
        return old.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    internal event Action<string>? CardinalityExceeded;
}
