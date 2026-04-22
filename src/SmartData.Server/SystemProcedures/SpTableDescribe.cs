using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpTableDescribe : SystemStoredProcedure<TableDescribeResult>
{
    public string Database { get; set; } = "";
    public string Name { get; set; } = "";

    public override TableDescribeResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.TableDescribe, Database);
        db.UseDatabase(Database);

        var columns = provider.Schema.GetColumns(Database, Name);
        var indexes = provider.Schema.GetIndexes(Database, Name);

        return new TableDescribeResult
        {
            Table = Name,
            Columns = columns.Select(c => new ColumnDetail
            {
                Name = c.Name,
                Type = c.Type,
                Nullable = c.IsNullable,
                Pk = c.IsPrimaryKey ? 1 : 0
            }).ToList(),
            Indexes = indexes.Select(i => new IndexDetail { Name = i.Name, Sql = i.Sql }).ToList()
        };
    }
}
