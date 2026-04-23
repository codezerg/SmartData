namespace SmartData.Cli.Commands;

public static class DbCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: sd db <list|create|drop|use> [args]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "list":
                await client.SendAndPrint("sp_database_list");
                break;

            case "create":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd db create <name>"); return; }
                await client.SendAndPrint("sp_database_create", new() { ["Name"] = rest[0] });
                break;

            case "drop":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd db drop <name>"); return; }
                await client.SendAndPrint("sp_database_drop", new() { ["Name"] = rest[0] });
                break;

            case "use":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: sd db use <name>"); return; }
                config.Database = rest[0];
                config.Save();
                Console.WriteLine($"Using database: {rest[0]}");
                break;

            default:
                Console.Error.WriteLine($"Unknown db command: {sub}");
                break;
        }
    }
}
