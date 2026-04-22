namespace SmartData.Cli.Commands;

public static class ConnectCommand
{
    public static void Run(string[] args, SdConfig config)
    {
        if (args.Length < 1)
        {
            if (!string.IsNullOrEmpty(config.Server))
                Console.WriteLine($"Connected to: {config.Server}");
            else
                Console.WriteLine("Not connected. Usage: ds connect <server>");
            return;
        }

        var server = args[0];
        if (!server.StartsWith("http"))
            server = $"http://{server}";

        config.Server = server;
        config.Save();
        Console.WriteLine($"Connected to {server}");
    }
}
