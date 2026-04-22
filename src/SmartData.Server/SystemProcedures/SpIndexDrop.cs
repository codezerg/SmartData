using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpIndexDrop : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Name { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.IndexDrop, Database);
        db.UseDatabase(Database);

        provider.SchemaOperations.DropIndex(Database, Name);
        return $"Index '{Name}' dropped.";
    }
}
