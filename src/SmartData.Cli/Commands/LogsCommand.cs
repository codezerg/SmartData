namespace SmartData.Cli.Commands;

public static class LogsCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        var limit = ArgParser.GetFlag(args, "--limit") ?? "50";
        await client.SendAndPrint("sp_logs", new() { ["Limit"] = limit });
    }

    public static async Task Errors(string[] args, SdConfig config, ApiClient client)
    {
        var name = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "";
        var limit = ArgParser.GetFlag(args, "--limit") ?? "50";
        await client.SendAndPrint("sp_errors", new() { ["Name"] = name, ["Limit"] = limit });
    }
}
