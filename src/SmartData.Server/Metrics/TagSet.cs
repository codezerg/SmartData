namespace SmartData.Server.Metrics;

/// <summary>
/// Immutable sorted set of key-value tags used as a dictionary key for metric series.
/// Each unique TagSet creates a separate time series within an instrument.
/// </summary>
internal readonly struct TagSet : IEquatable<TagSet>
{
    public static readonly TagSet Empty = new([]);

    private readonly (string Key, string Value)[] _tags;
    private readonly int _hashCode;

    public TagSet(params (string Key, string Value)[] tags)
    {
        if (tags.Length == 0)
        {
            _tags = [];
            _hashCode = 0;
            return;
        }

        // Sort by key for consistent equality regardless of input order
        var sorted = new (string Key, string Value)[tags.Length];
        Array.Copy(tags, sorted, tags.Length);
        Array.Sort(sorted, (a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
        _tags = sorted;

        // Precompute hash
        var hash = new HashCode();
        foreach (var (key, value) in _tags)
        {
            hash.Add(key);
            hash.Add(value);
        }
        _hashCode = hash.ToHashCode();
    }

    public ReadOnlySpan<(string Key, string Value)> Tags => _tags ?? [];

    public bool Equals(TagSet other)
    {
        var mine = _tags ?? [];
        var theirs = other._tags ?? [];

        if (mine.Length != theirs.Length)
            return false;

        for (var i = 0; i < mine.Length; i++)
        {
            if (mine[i].Key != theirs[i].Key || mine[i].Value != theirs[i].Value)
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is TagSet other && Equals(other);
    public override int GetHashCode() => _hashCode;

    public override string ToString()
    {
        if (_tags == null || _tags.Length == 0)
            return "";

        return string.Join(",", _tags.Select(t => $"{t.Key}={t.Value}"));
    }

    public static bool operator ==(TagSet left, TagSet right) => left.Equals(right);
    public static bool operator !=(TagSet left, TagSet right) => !left.Equals(right);
}
