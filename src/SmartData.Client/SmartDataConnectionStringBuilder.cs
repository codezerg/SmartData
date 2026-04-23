using System.Data.Common;

namespace SmartData.Client;

public sealed class SmartDataConnectionStringBuilder : DbConnectionStringBuilder
{
    public const string ServerKey = "Server";
    public const string UserIdKey = "User Id";
    public const string PasswordKey = "Password";
    public const string TokenKey = "Token";
    public const string TimeoutKey = "Timeout";

    private static readonly Dictionary<string, string> KeyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UID"] = UserIdKey,
        ["Username"] = UserIdKey,
        ["User"] = UserIdKey,
        ["PWD"] = PasswordKey,
    };

    public SmartDataConnectionStringBuilder() { }

    public SmartDataConnectionStringBuilder(string? connectionString)
    {
        if (!string.IsNullOrEmpty(connectionString))
            ConnectionString = connectionString;
    }

    public string? Server
    {
        get => GetString(ServerKey);
        set => SetOrRemove(ServerKey, value);
    }

    public string? UserId
    {
        get => GetString(UserIdKey);
        set => SetOrRemove(UserIdKey, value);
    }

    public string? Password
    {
        get => GetString(PasswordKey);
        set => SetOrRemove(PasswordKey, value);
    }

    public string? Token
    {
        get => GetString(TokenKey);
        set => SetOrRemove(TokenKey, value);
    }

    public int Timeout
    {
        get => GetString(TimeoutKey) is { } v && int.TryParse(v, out var i) ? i : 30;
        set => base[TimeoutKey] = value.ToString();
    }

#pragma warning disable CS8765
    public override object this[string keyword]
    {
        get => base[Normalize(keyword)];
        set => base[Normalize(keyword)] = value!;
    }
#pragma warning restore CS8765

    public override bool ContainsKey(string keyword) => base.ContainsKey(Normalize(keyword));

    public override bool Remove(string keyword) => base.Remove(Normalize(keyword));

    private static string Normalize(string keyword)
        => KeyAliases.TryGetValue(keyword, out var canonical) ? canonical : keyword;

    private string? GetString(string key)
        => base.TryGetValue(key, out var v) ? v?.ToString() : null;

    private void SetOrRemove(string key, string? value)
    {
        if (string.IsNullOrEmpty(value))
            base.Remove(key);
        else
            base[key] = value;
    }
}
