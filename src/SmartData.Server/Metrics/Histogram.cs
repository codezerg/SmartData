using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SmartData.Server.Metrics;

/// <summary>
/// Histogram for recording distributions (e.g., request durations).
/// Lock-free design using Interlocked operations and reservoir sampling.
/// </summary>
internal sealed class Histogram
{
    private ConcurrentDictionary<TagSet, HistogramBucket> _buckets = new();
    private readonly int _maxSeries;
    private volatile bool _cardinalityWarned;

    public string Name { get; }

    public Histogram(string name, int maxSeries)
    {
        Name = name;
        _maxSeries = maxSeries;
    }

    public void Record(double value, params (string Key, string Value)[] tags)
    {
        var tagSet = tags.Length == 0 ? TagSet.Empty : new TagSet(tags);
        RecordInternal(tagSet, value);
    }

    internal void Record(double value, TagSet tagSet)
    {
        RecordInternal(tagSet, value);
    }

    private void RecordInternal(TagSet tagSet, double value)
    {
        if (!_buckets.ContainsKey(tagSet) && _buckets.Count >= _maxSeries)
        {
            if (!_cardinalityWarned)
            {
                _cardinalityWarned = true;
                CardinalityExceeded?.Invoke(Name);
            }
            return;
        }

        var bucket = _buckets.GetOrAdd(tagSet, _ => new HistogramBucket());
        bucket.Record(value);
    }

    public List<HistogramSnapshot> Snapshot()
    {
        return _buckets.Select(kv =>
        {
            var snap = kv.Value.GetSnapshot();
            return new HistogramSnapshot(kv.Key, snap.Count, snap.Sum, snap.Min, snap.Max, snap.P50, snap.P95, snap.P99);
        }).ToList();
    }

    public List<HistogramSnapshot> CollectAndReset()
    {
        var old = Interlocked.Exchange(ref _buckets, new ConcurrentDictionary<TagSet, HistogramBucket>());
        return old.Select(kv =>
        {
            var snap = kv.Value.GetSnapshot();
            return new HistogramSnapshot(kv.Key, snap.Count, snap.Sum, snap.Min, snap.Max, snap.P50, snap.P95, snap.P99);
        }).ToList();
    }

    internal event Action<string>? CardinalityExceeded;
}

internal readonly record struct HistogramSnapshot(
    TagSet Tags, long Count, double Sum, double Min, double Max,
    double P50, double P95, double P99);

/// <summary>
/// Lock-free histogram bucket with reservoir sampling for percentile estimation.
/// </summary>
internal sealed class HistogramBucket
{
    private const int ReservoirCapacity = 1024;

    private long _count;
    private long _sumBits;  // double stored as long bits for Interlocked
    private long _minBits;
    private long _maxBits;
    private readonly double[] _reservoir = new double[ReservoirCapacity];
    private long _totalSeen; // total records seen (for reservoir sampling)

    public HistogramBucket()
    {
        _sumBits = BitConverter.DoubleToInt64Bits(0.0);
        _minBits = BitConverter.DoubleToInt64Bits(double.MaxValue);
        _maxBits = BitConverter.DoubleToInt64Bits(double.MinValue);
    }

    public void Record(double value)
    {
        Interlocked.Increment(ref _count);
        var index = Interlocked.Increment(ref _totalSeen) - 1;

        // Sum: CAS loop
        InterlockedAddDouble(ref _sumBits, value);

        // Min: CAS loop
        InterlockedMin(ref _minBits, value);

        // Max: CAS loop
        InterlockedMax(ref _maxBits, value);

        // Reservoir sampling (Vitter's Algorithm R)
        if (index < ReservoirCapacity)
        {
            _reservoir[index] = value;
        }
        else
        {
            // Random replacement with decreasing probability
            var j = Random.Shared.NextInt64(0, index + 1);
            if (j < ReservoirCapacity)
                _reservoir[j] = value;
        }
    }

    public (long Count, double Sum, double Min, double Max, double P50, double P95, double P99) GetSnapshot()
    {
        var count = Interlocked.Read(ref _count);
        var sum = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _sumBits));
        var min = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _minBits));
        var max = BitConverter.Int64BitsToDouble(Interlocked.Read(ref _maxBits));

        if (count == 0)
            return (0, 0, 0, 0, 0, 0, 0);

        // Compute percentiles from reservoir
        var sampleCount = (int)Math.Min(count, ReservoirCapacity);
        var samples = new double[sampleCount];
        Array.Copy(_reservoir, samples, sampleCount);
        Array.Sort(samples);

        var p50 = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        var p99 = Percentile(samples, 0.99);

        return (count, sum, min, max, p50, p95, p99);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var index = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return sorted[lower];
        var fraction = index - lower;
        return sorted[lower] * (1 - fraction) + sorted[upper] * fraction;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InterlockedAddDouble(ref long bits, double value)
    {
        long original, newBits;
        do
        {
            original = Interlocked.Read(ref bits);
            var current = BitConverter.Int64BitsToDouble(original);
            newBits = BitConverter.DoubleToInt64Bits(current + value);
        } while (Interlocked.CompareExchange(ref bits, newBits, original) != original);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InterlockedMin(ref long bits, double value)
    {
        long original;
        do
        {
            original = Interlocked.Read(ref bits);
            var current = BitConverter.Int64BitsToDouble(original);
            if (value >= current) return;
        } while (Interlocked.CompareExchange(ref bits, BitConverter.DoubleToInt64Bits(value), original) != original);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InterlockedMax(ref long bits, double value)
    {
        long original;
        do
        {
            original = Interlocked.Read(ref bits);
            var current = BitConverter.Int64BitsToDouble(original);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref bits, BitConverter.DoubleToInt64Bits(value), original) != original);
    }
}
