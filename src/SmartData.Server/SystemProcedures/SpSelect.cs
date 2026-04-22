using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpSelect : SystemStoredProcedure<List<Dictionary<string, object?>>>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Where { get; set; } = "";
    public string OrderBy { get; set; } = "";
    public int Limit { get; set; }
    public int Offset { get; set; }

    public override List<Dictionary<string, object?>> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.DataSelect, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrEmpty(Table))
            RaiseError("Table is required.");

        var where = QueryFilterBuilder.Parse(string.IsNullOrEmpty(Where) ? null : Where);
        return provider.RawData.Select(Database, Table, where, OrderBy, Limit, Offset);
    }
}
