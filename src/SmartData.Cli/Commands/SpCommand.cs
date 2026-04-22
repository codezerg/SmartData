namespace SmartData.Cli.Commands;

public static class SpCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: sd sp <exec|errors> [args]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "errors":
                await LogsCommand.Errors(rest, config, client);
                break;

            default:
                Console.Error.WriteLine($"Unknown sp command: {sub}");
                break;
        }
    }
}
