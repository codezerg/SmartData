using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpColumnList : SystemStoredProcedure<List<ColumnDetail>>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";

    public override List<ColumnDetail> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.ColumnList, Database);
        db.UseDatabase(Database);

        var columns = provider.Schema.GetColumns(Database, Table);

        return columns.Select(c => new ColumnDetail
        {
            Name = c.Name,
            Type = c.Type,
            Nullable = c.IsNullable,
            Pk = c.IsPrimaryKey ? 1 : 0
        }).ToList();
    }
}
