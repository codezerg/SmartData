using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpColumnDrop : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.ColumnDrop, Database);
        db.UseDatabase(Database);

        provider.SchemaOperations.DropColumn(Database, Table, Name);
        return $"Column '{Name}' dropped from '{Table}'.";
    }
}
