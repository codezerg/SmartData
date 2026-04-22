using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpColumnAdd : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public bool Nullable { get; set; }

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.ColumnAdd, Database);
        db.UseDatabase(Database);

        var sqlType = provider.SchemaOperations.MapType(Type);
        provider.SchemaOperations.AddColumn(Database, Table, Name, sqlType, Nullable);

        return $"Column '{Name}' added to '{Table}'.";
    }
}
