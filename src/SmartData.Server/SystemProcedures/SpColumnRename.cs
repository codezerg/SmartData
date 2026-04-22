using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpColumnRename : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";
    public string NewName { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.ColumnRename, Database);
        db.UseDatabase(Database);

        provider.SchemaOperations.RenameColumn(Database, Table, Name, NewName);
        return $"Column renamed from '{Name}' to '{NewName}' in '{Table}'.";
    }
}
