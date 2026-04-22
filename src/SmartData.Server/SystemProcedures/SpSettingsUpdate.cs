using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpSettingsUpdate : SystemStoredProcedure<string>
{
    private readonly SettingsService _settings;

    public string Key { get; set; } = "";
    public string Value { get; set; } = "";

    public SpSettingsUpdate(SettingsService settings)
    {
        _settings = settings;
    }

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.ServerSettings);

        if (string.IsNullOrWhiteSpace(Key))
            RaiseError("Key is required.");

        _settings.Update(Key, Value);
        return $"Setting '{Key}' updated.";
    }
}
