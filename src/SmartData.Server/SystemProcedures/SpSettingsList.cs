using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpSettingsList : SystemStoredProcedure<SettingsResult>
{
    private readonly SettingsService _settings;

    public SpSettingsList(SettingsService settings)
    {
        _settings = settings;
    }

    public override SettingsResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.ServerSettings);

        var entries = _settings.GetAll();
        return new SettingsResult
        {
            Items = entries.Select(e => new SettingsResultItem
            {
                Key = e.Key,
                Section = e.Section,
                DisplayName = e.DisplayName,
                Value = e.Value,
                Type = e.PropertyType.Name,
                IsReadOnly = e.IsReadOnly,
                RestartRequired = e.RestartRequired,
                ModifiedAt = e.ModifiedAt
            }).ToList()
        };
    }
}
