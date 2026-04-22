using System.Text.Json;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpTableCreate : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";
    public string Name { get; set; } = "";
    public string Columns { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.TableCreate, Database);
        db.UseDatabase(Database);

        var columns = JsonSerializer.Deserialize<List<ColumnDefInput>>(Columns,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var defs = columns.Select(c => new ColumnDefinition(c.Name, c.Type, c.Nullable, c.Pk)).ToList();

        provider.SchemaOperations.CreateTable(Database, Name, defs);

        return $"Table '{Name}' created.";
    }

    private class ColumnDefInput
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool Nullable { get; set; }
        public bool Pk { get; set; }
    }
}
