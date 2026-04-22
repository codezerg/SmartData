using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpDatabaseDrop : SystemStoredProcedure<string>
{
    public string Name { get; set; } = "";

    private readonly DatabaseManager _dbManager;

    public SpDatabaseDrop(DatabaseManager dbManager) => _dbManager = dbManager;

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.DatabaseDrop);
        _dbManager.DropDatabase(Name);
        return $"Database '{Name}' dropped.";
    }
}
