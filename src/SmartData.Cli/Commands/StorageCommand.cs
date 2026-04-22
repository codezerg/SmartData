namespace SmartData.Cli.Commands;

public static class StorageCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        var spArgs = new Dictionary<string, object>();

        var db = ArgParser.GetFlag(args, "--db");
        if (!string.IsNullOrEmpty(db))
            spArgs["Database"] = db;

        await client.SendAndPrint("sp_storage", spArgs);
    }
}
