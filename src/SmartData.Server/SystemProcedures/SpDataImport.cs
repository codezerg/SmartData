using System.Text.Json;
using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpDataImport : SystemStoredProcedure<object>
{
    public string Database { get; set; } = "";
    public string Table { get; set; } = "";
    public string Rows { get; set; } = "";
    public string Mode { get; set; } = "insert";
    public bool Truncate { get; set; }
    public bool DryRun { get; set; }

    public override object Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.DataImport, Database);
        db.UseDatabase(Database);

        if (string.IsNullOrEmpty(Table))
            RaiseError("Table is required.");
        if (string.IsNullOrEmpty(Rows))
            RaiseError("Rows is required.");

        var raw = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(Rows,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (raw == null) RaiseError("Rows must be a JSON array of objects.");

        var rows = raw.Select(r => r.ToDictionary(kv => kv.Key, kv => UnwrapJsonElement(kv.Value))).ToList();

        if (rows.Count == 0)
            return new DataImportResult { Table = Table };

        if (DryRun)
        {
            var columns = rows[0].Keys.ToList();
            return new DataImportPreview { Table = Table, Rows = rows.Count, Columns = columns };
        }

        return provider.RawData.Import(Database, Table, rows, Mode, Truncate);
    }

    private static object? UnwrapJsonElement(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => el.GetRawText()
    };
}
