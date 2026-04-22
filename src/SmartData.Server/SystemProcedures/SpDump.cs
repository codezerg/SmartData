using System.Text;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpDump : SystemStoredProcedure<string>
{
    public string Database { get; set; } = "";

    public override string Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.RequireScoped(Permissions.DataDump, Database);
        db.UseDatabase(Database);

        var sb = new StringBuilder();
        sb.AppendLine($"# {Database}");
        sb.AppendLine();

        var allTables = provider.Schema.GetTables(Database)
            .Where(t => !t.Name.StartsWith("_sys_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        sb.AppendLine("## Tables");
        sb.AppendLine();

        foreach (var table in allTables)
        {
            sb.AppendLine($"### {table.Name}");

            var columns = provider.Schema.GetColumns(Database, table.Name);
            foreach (var col in columns)
            {
                var parts = new List<string> { col.Type };
                if (col.IsPrimaryKey) parts.Add("PK");
                if (!col.IsNullable) parts.Add("NOT NULL");
                sb.AppendLine($"- {col.Name} ({string.Join(", ", parts)})");
            }

            var indexes = provider.Schema.GetIndexes(Database, table.Name).ToList();
            if (indexes.Count > 0)
            {
                sb.AppendLine("- Indexes:");
                foreach (var idx in indexes)
                    sb.AppendLine($"  - {idx.Name}: {idx.Sql}");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
