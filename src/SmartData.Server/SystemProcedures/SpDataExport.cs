using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpDataExport : SystemStoredProcedure<DataExportResult>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Where { get; set; } = "";

    public override DataExportResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.DataExport, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrEmpty(Table))
            RaiseError("Table is required.");

        var where = QueryFilterBuilder.Parse(string.IsNullOrEmpty(Where) ? null : Where);
        var rows = provider.RawData.Select(Database, Table, where, null, 0, 0);

        return new DataExportResult { Table = Table, Count = rows.Count, Rows = rows };
    }
}
