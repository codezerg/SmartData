namespace SmartData.Cli.Commands;

public static class UserCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: sd user <create> [args]");
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "create":
                await Create(rest, client);
                break;

            default:
                Console.Error.WriteLine($"Unknown user command: {sub}");
                break;
        }
    }

    private static async Task Create(string[] args, ApiClient client)
    {
        var username = ArgParser.GetFlag(args, "--username");
        var password = ArgParser.GetFlag(args, "--password");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Usage: sd user create --username x --password y");
            return;
        }

        var spArgs = new Dictionary<string, object>
        {
            ["Username"] = username,
            ["Password"] = password
        };

        await client.SendAndPrint("sp_user_create", spArgs);
    }
}
