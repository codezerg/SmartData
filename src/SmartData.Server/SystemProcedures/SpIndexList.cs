using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpIndexList : SystemStoredProcedure<List<IndexDetail>>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";

    public override List<IndexDetail> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.IndexList, Database);
        db.UseDatabase(Database);

        var indexes = provider.Schema.GetIndexes(Database, Table);
        return indexes.Select(i => new IndexDetail { Name = i.Name, Sql = i.Sql }).ToList();
    }
}
