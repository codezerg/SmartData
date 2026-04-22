using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpIndexCreate : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";
    public string Columns { get; set; } = "";
    public bool Unique { get; set; }

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.IndexCreate, Database);
        db.UseDatabase(Database);

        provider.SchemaOperations.CreateIndex(Database, Table, Name, Columns, Unique);
        return $"Index '{Name}' created on '{Table}'.";
    }
}
