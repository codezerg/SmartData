using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpInsert : SystemStoredProcedure<object>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Values { get; set; } = "";

    public override object Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.DataInsert, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrEmpty(Table))
            RaiseError("Table is required.");

        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(Values);
        if (dict == null) RaiseError("Values is required.");

        return provider.RawData.Insert(Database, Table, dict);
    }
}
