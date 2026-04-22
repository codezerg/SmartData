using LinqToDB;
using Microsoft.Extensions.Options;
using SmartData.Server.Backup;
using SmartData.Server.Entities;
using SmartData.Server.Metrics;
using SmartData.Server.Providers;

namespace SmartData.Server;

public record SettingDescriptor(
    string Key,
    string Section,
    string DisplayName,
    Type PropertyType,
    bool IsReadOnly,
    bool RestartRequired,
    Func<SmartDataOptions, string> Getter,
    Action<SmartDataOptions, string>? Setter);

public record SettingEntry(
    string Key,
    string Section,
    string DisplayName,
    string Value,
    Type PropertyType,
    bool IsReadOnly,
    bool RestartRequired,
    DateTime? ModifiedAt);

public class SettingsService
{
    private readonly IDatabaseProvider _provider;
    private readonly SmartDataOptions _options;

    private static readonly List<SettingDescriptor> _descriptors = BuildDescriptors();

    public SettingsService(IDatabaseProvider provider, IOptions<SmartDataOptions> options)
    {
        _provider = provider;
        _options = options.Value;
    }

    public IReadOnlyList<SettingDescriptor> Descriptors => _descriptors;

    public void LoadFromDatabase()
    {
        using var db = _provider.OpenConnection("master");
        var rows = db.GetTable<SysSetting>().ToList();

        foreach (var row in rows)
        {
            var desc = _descriptors.Find(d => d.Key == row.Key);
            if (desc is { IsReadOnly: false, Setter: not null })
            {
                try { desc.Setter(_options, row.Value); }
                catch { /* skip invalid values */ }
            }
        }
    }

    public List<SettingEntry> GetAll()
    {
        Dictionary<string, SysSetting> dbRows;
        using (var db = _provider.OpenConnection("master"))
            dbRows = db.GetTable<SysSetting>().ToDictionary(r => r.Key);

        return _descriptors.Select(d => new SettingEntry(
            d.Key,
            d.Section,
            d.DisplayName,
            d.Getter(_options),
            d.PropertyType,
            d.IsReadOnly,
            d.RestartRequired,
            dbRows.TryGetValue(d.Key, out var row) ? row.ModifiedAt : null
        )).ToList();
    }

    public void Update(string key, string value)
    {
        var desc = _descriptors.Find(d => d.Key == key)
            ?? throw new InvalidOperationException($"Unknown setting: {key}");
        if (desc.IsReadOnly)
            throw new InvalidOperationException($"Setting '{key}' is read-only.");

        // Validate by parsing
        desc.Setter!(_options, value);

        // Persist
        using var db = _provider.OpenConnection("master");
        var existing = db.GetTable<SysSetting>().FirstOrDefault(s => s.Key == key);
        if (existing != null)
        {
            existing.Value = value;
            existing.ModifiedAt = DateTime.UtcNow;
            db.Update(existing);
        }
        else
        {
            db.Insert(new SysSetting
            {
                Key = key,
                Value = value,
                ModifiedAt = DateTime.UtcNow
            });
        }
    }

    private static List<SettingDescriptor> BuildDescriptors()
    {
        return
        [
            // Root (SchemaMode applied on startup only — changes take effect after restart)
            Editable("SchemaMode", "General", "Schema Mode", typeof(SchemaMode),
                o => o.SchemaMode.ToString(),
                (o, v) => o.SchemaMode = Enum.Parse<SchemaMode>(v, true), restartRequired: true),
            Editable("IncludeExceptionDetails", "General", "Include Exception Details", typeof(bool),
                o => o.IncludeExceptionDetails.ToString(),
                (o, v) => o.IncludeExceptionDetails = bool.Parse(v)),

            // Index (applied on startup only — changes take effect after restart)
            Editable("Index.Prefix", "Index", "Prefix", typeof(string),
                o => o.Index.Prefix,
                (o, v) => o.Index.Prefix = v, restartRequired: true),
            Editable("Index.AutoDrop", "Index", "Auto Drop", typeof(bool),
                o => o.Index.AutoDrop.ToString(),
                (o, v) => o.Index.AutoDrop = bool.Parse(v), restartRequired: true),
            Editable("Index.AutoCreate", "Index", "Auto Create", typeof(bool),
                o => o.Index.AutoCreate.ToString(),
                (o, v) => o.Index.AutoCreate = bool.Parse(v), restartRequired: true),
            Editable("Index.AutoCreateFullText", "Index", "Auto Create Full-Text", typeof(bool),
                o => o.Index.AutoCreateFullText.ToString(),
                (o, v) => o.Index.AutoCreateFullText = bool.Parse(v), restartRequired: true),

            // Metrics
            Editable("Metrics.Enabled", "Metrics", "Enabled", typeof(bool),
                o => o.Metrics.Enabled.ToString(),
                (o, v) => o.Metrics.Enabled = bool.Parse(v), restartRequired: true),
            Editable("Metrics.TraceSampleRate", "Metrics", "Trace Sample Rate", typeof(double),
                o => o.Metrics.TraceSampleRate.ToString(),
                (o, v) => o.Metrics.TraceSampleRate = double.Parse(v)),
            Editable("Metrics.FlushIntervalSeconds", "Metrics", "Flush Interval (seconds)", typeof(int),
                o => o.Metrics.FlushIntervalSeconds.ToString(),
                (o, v) => o.Metrics.FlushIntervalSeconds = int.Parse(v)),

            // Backup
            EditableNullableInt("Backup.MaxBackupAge", "Backup", "Max Backup Age (days)",
                o => o.Backup.MaxBackupAge, (o, v) => o.Backup.MaxBackupAge = v),
            EditableNullableInt("Backup.MaxBackupCount", "Backup", "Max Backup Count",
                o => o.Backup.MaxBackupCount, (o, v) => o.Backup.MaxBackupCount = v),
            EditableNullableInt("Backup.MaxHistoryAge", "Backup", "Max History Age (days)",
                o => o.Backup.MaxHistoryAge, (o, v) => o.Backup.MaxHistoryAge = v),
            EditableNullableInt("Backup.MaxHistoryCount", "Backup", "Max History Count",
                o => o.Backup.MaxHistoryCount, (o, v) => o.Backup.MaxHistoryCount = v),

            // Session
            Editable("Session.SessionTtl", "Session", "Session TTL", typeof(TimeSpan),
                o => o.Session.SessionTtl.ToString(),
                (o, v) => o.Session.SessionTtl = TimeSpan.Parse(v)),
            Editable("Session.SlidingExpiration", "Session", "Sliding Expiration", typeof(bool),
                o => o.Session.SlidingExpiration.ToString(),
                (o, v) => o.Session.SlidingExpiration = bool.Parse(v)),
            Editable("Session.CleanupIntervalSeconds", "Session", "Cleanup Interval (seconds)", typeof(int),
                o => o.Session.CleanupIntervalSeconds.ToString(),
                (o, v) => o.Session.CleanupIntervalSeconds = int.Parse(v)),
        ];
    }

    private static SettingDescriptor ReadOnly(string key, string section, string name, Type type,
        Func<SmartDataOptions, string> getter) =>
        new(key, section, name, type, true, false, getter, null);

    private static SettingDescriptor Editable(string key, string section, string name, Type type,
        Func<SmartDataOptions, string> getter, Action<SmartDataOptions, string> setter, bool restartRequired = false) =>
        new(key, section, name, type, false, restartRequired, getter, setter);

    private static SettingDescriptor EditableNullableInt(string key, string section, string name,
        Func<SmartDataOptions, int?> getter, Action<SmartDataOptions, int?> setter) =>
        new(key, section, name, typeof(int?), false, false,
            o => getter(o)?.ToString() ?? "",
            (o, v) => setter(o, string.IsNullOrWhiteSpace(v) ? null : int.Parse(v)));
}
