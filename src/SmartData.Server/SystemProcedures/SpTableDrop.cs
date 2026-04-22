using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpTableDrop : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Name { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.TableDrop, Database);
        db.UseDatabase(Database);

        provider.SchemaOperations.DropTable(Database, Name);
        return $"Table '{Name}' dropped.";
    }
}
