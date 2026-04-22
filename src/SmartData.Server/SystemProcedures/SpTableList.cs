using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpTableList : SystemStoredProcedure<List<TableListItem>>
{
    public string Database { get; set; } = "";

    public override List<TableListItem> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.TableList, Database);
        db.UseDatabase(Database);

        var tables = provider.Schema.GetTables(Database);

        return tables.Select(t => new TableListItem
        {
            Name = t.Name,
            ColumnCount = t.ColumnCount,
            RowCount = t.RowCount
        }).ToList();
    }
}
