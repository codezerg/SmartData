using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpTableRename : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Name { get; set; } = "";
    public string NewName { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.TableRename, Database);
        db.UseDatabase(Database);

        provider.SchemaOperations.RenameTable(Database, Name, NewName);
        return $"Table renamed from '{Name}' to '{NewName}'.";
    }
}
