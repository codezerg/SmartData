namespace SmartData.Cli.Commands;

public static class ColumnCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: ds column <add|drop|rename> [args]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "add":
                if (rest.Length < 3) { Console.Error.WriteLine("Usage: ds column add <table> <name> <type> [--nullable]"); return; }
                var nullable = ArgParser.HasFlag(rest, "--nullable");
                await client.SendAndPrint("sp_column_add", new()
                {
                    ["Table"] = rest[0],
                    ["Name"] = rest[1],
                    ["Type"] = rest[2].TrimEnd('?'),
                    ["Nullable"] = nullable || rest[2].EndsWith('?')
                });
                break;

            case "drop":
                if (rest.Length < 2) { Console.Error.WriteLine("Usage: ds column drop <table> <name>"); return; }
                await client.SendAndPrint("sp_column_drop", new() { ["Table"] = rest[0], ["Name"] = rest[1] });
                break;

            case "rename":
                if (rest.Length < 3) { Console.Error.WriteLine("Usage: ds column rename <table> <name> <new-name>"); return; }
                await client.SendAndPrint("sp_column_rename", new() { ["Table"] = rest[0], ["Name"] = rest[1], ["NewName"] = rest[2] });
                break;

            default:
                Console.Error.WriteLine($"Unknown column command: {sub}");
                break;
        }
    }
}
