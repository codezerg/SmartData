using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpDatabaseCreate : SystemStoredProcedure<string>
{
    public string Name { get; set; } = "";

    private readonly DatabaseManager _dbManager;

    public SpDatabaseCreate(DatabaseManager dbManager) => _dbManager = dbManager;

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.DatabaseCreate);

        if (string.IsNullOrWhiteSpace(Name))
            RaiseError("Database name is required.");

        if (string.Equals(Name, "master", StringComparison.OrdinalIgnoreCase))
            RaiseError("Cannot create a database named 'master'.");

        if (_dbManager.DatabaseExists(Name))
            RaiseError($"Database '{Name}' already exists.");

        _dbManager.CreateDatabase(Name);
        return $"Database '{Name}' created.";
    }
}
