using SmartData.Client;

namespace SmartData.Cli.Commands;

public static class ConnectCommand
{
    public static void Run(string[] args, SdConfig config)
    {
        var builder = new SmartDataConnectionStringBuilder(config.ConnectionString);

        if (args.Length < 1)
        {
            if (!string.IsNullOrEmpty(builder.Server))
                Console.WriteLine($"Connected to: {builder.Server}");
            else
                Console.WriteLine("Not connected. Usage: sd connect <server>");
            return;
        }

        var server = args[0];
        if (!server.Contains("://", StringComparison.Ordinal))
            server = $"http://{server}";

        builder.Server = server;
        builder.Token = null;
        builder.UserId = null;
        builder.Password = null;
        config.ConnectionString = builder.ConnectionString;
        config.Save();

        Console.WriteLine($"Connected to {server}");
    }
}
