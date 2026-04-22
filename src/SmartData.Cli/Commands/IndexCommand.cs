namespace SmartData.Cli.Commands;

public static class IndexCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: ds index <list|create|drop> [args]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "list":
                if (rest.Length < 1) { Console.Error.WriteLine("Usage: ds index list <table>"); return; }
                await client.SendAndPrint("sp_index_list", new() { ["Table"] = rest[0] });
                break;

            case "create":
                if (rest.Length < 2) { Console.Error.WriteLine("Usage: ds index create <table> <name> --columns Col1,Col2 [--unique]"); return; }
                var columns = ArgParser.GetFlag(rest, "--columns") ?? "";
                var unique = ArgParser.HasFlag(rest, "--unique");
                await client.SendAndPrint("sp_index_create", new()
                {
                    ["Table"] = rest[0],
                    ["Name"] = rest[1],
                    ["Columns"] = columns,
                    ["Unique"] = unique
                });
                break;

            case "drop":
                if (rest.Length < 2) { Console.Error.WriteLine("Usage: ds index drop <table> <name>"); return; }
                await client.SendAndPrint("sp_index_drop", new() { ["Name"] = rest[1] });
                break;

            default:
                Console.Error.WriteLine($"Unknown index command: {sub}");
                break;
        }
    }
}
