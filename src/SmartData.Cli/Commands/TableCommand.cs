using System.Text.Json;

namespace SmartData.Cli.Commands;

public static class TableCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: ds table <list|create|drop|rename|describe> [args]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "list":
                await client.SendAndPrint("sp_table_list");
                break;

            case "create":
                await Create(rest, client);
                break;

            case "drop":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: ds table drop <name>"); return; }
                await client.SendAndPrint("sp_table_drop", new() { ["Name"] = rest[0] });
                break;

            case "rename":
                if (rest.Length < 2) { Console.Error.WriteLine("Usage: ds table rename <name> <new-name>"); return; }
                await client.SendAndPrint("sp_table_rename", new() { ["Name"] = rest[0], ["NewName"] = rest[1] });
                break;

            case "describe":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: ds table describe <name>"); return; }
                await client.SendAndPrint("sp_table_describe", new() { ["Name"] = rest[0] });
                break;

            default:
                Console.Error.WriteLine($"Unknown table command: {sub}");
                break;
        }
    }

    private static async Task Create(string[] args, ApiClient client)
    {
        if (args.Length < 1) { Console.Error.WriteLine("Usage: ds table create <name> [--col Name:Type[:pk] ...]"); return; }

        var name = args[0];
        var columns = new List<object>();

        var colValues = ArgParser.GetAllFlags(args, "--col");
        foreach (var colDef in colValues)
        {
            var parts = colDef.Split(':');
            if (parts.Length < 2) continue;

            var colName = parts[0];
            var colType = parts[1].TrimEnd('?');
            var nullable = parts[1].EndsWith('?');
            var pk = parts.Length > 2 && parts[2].Equals("pk", StringComparison.OrdinalIgnoreCase);

            columns.Add(new { name = colName, type = colType, nullable, pk });
        }

        var columnsJson = JsonSerializer.Serialize(columns);
        await client.SendAndPrint("sp_table_create", new() { ["Name"] = name, ["Columns"] = columnsJson });
    }
}
