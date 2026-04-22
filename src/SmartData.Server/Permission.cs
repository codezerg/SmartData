using System;
using System.Collections.Generic;
using System.Text;

namespace SmartData.Server;

public sealed class Permission : IEquatable<Permission>
{
    public string Key { get; }
    public string Description { get; }

    public IReadOnlyList<string> Segments { get; }
    public string Action => Segments[^1];

    public Permission(string key, string description)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Segments = key.Split(':');
    }

    public bool Equals(Permission? other) => other is not null && Key == other.Key;
    public override bool Equals(object? obj) => obj is Permission p && Equals(p);
    public override int GetHashCode() => Key.GetHashCode();
    public override string ToString() => $"{Key} — {Description}";
}
