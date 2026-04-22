namespace SmartData.Cli.Commands;

public static class SettingsCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "list":
                await client.SendAndPrint("sp_settings_list");
                break;

            case "get":
                await Get(rest, client);
                break;

            case "set":
                await Set(rest, client);
                break;

            default:
                Console.Error.WriteLine($"Unknown settings command: {sub}");
                PrintUsage();
                break;
        }
    }

    private static async Task Get(string[] args, ApiClient client)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: sd settings get <key>");
            return;
        }

        var result = await client.SendAsync("sp_settings_list");
        if (!result.Success)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return;
        }

        var json = result.GetDataAsJson();
        if (json == null) return;

        // Parse and filter to the requested key
        var doc = System.Text.Json.JsonDocument.Parse(json);
        var key = args[0];
        foreach (var item in doc.RootElement.GetProperty("Items").EnumerateArray())
        {
            if (item.GetProperty("Key").GetString()?.Equals(key, StringComparison.OrdinalIgnoreCase) == true)
            {
                Console.WriteLine($"{item.GetProperty("Key").GetString()} = {item.GetProperty("Value").GetString()}");
                return;
            }
        }

        Console.Error.WriteLine($"Setting '{key}' not found.");
    }

    private static async Task Set(string[] args, ApiClient client)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: sd settings set <key> <value>");
            return;
        }

        await client.SendAndPrint("sp_settings_update", new Dictionary<string, object>
        {
            ["Key"] = args[0],
            ["Value"] = args[1]
        });
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: sd settings <command>");
        Console.WriteLine();
        Console.WriteLine("  list                          List all settings");
        Console.WriteLine("  get <key>                     Get a setting value");
        Console.WriteLine("  set <key> <value>             Update a setting");
    }
}
