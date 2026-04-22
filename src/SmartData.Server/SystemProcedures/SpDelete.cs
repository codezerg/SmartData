using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpDelete : SystemStoredProcedure<int>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Where { get; set; } = "";

    public override int Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.DataDelete, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrEmpty(Table))
            RaiseError("Table is required.");
        if (string.IsNullOrEmpty(Where))
            RaiseError("Where is required for deletes.");

        var where = QueryFilterBuilder.Parse(Where)!;
        return provider.RawData.Delete(Database, Table, where);
    }
}
