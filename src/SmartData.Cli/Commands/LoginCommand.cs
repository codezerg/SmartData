using SmartData.Client;

namespace SmartData.Cli.Commands;

public static class LoginCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (string.IsNullOrEmpty(config.ConnectionString))
        {
            Console.Error.WriteLine("Not connected. Run: sd connect <server>");
            return;
        }

        var builder = new SmartDataConnectionStringBuilder(config.ConnectionString);
        if (string.IsNullOrEmpty(builder.Server))
        {
            Console.Error.WriteLine("Not connected. Run: sd connect <server>");
            return;
        }

        var username = ArgParser.GetFlag(args, "--username");
        var password = ArgParser.GetFlag(args, "--password");

        if (string.IsNullOrEmpty(username))
        {
            Console.Write("Username: ");
            username = Console.ReadLine()?.Trim();
        }

        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Password: ");
            password = Console.ReadLine()?.Trim();
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine("Username and password required.");
            return;
        }

        builder.Token = null;
        builder.UserId = username;
        builder.Password = password;

        await using var conn = new SmartDataConnection(builder.ConnectionString);
        try
        {
            await conn.OpenAsync();
        }
        catch (SmartDataException ex)
        {
            Console.Error.WriteLine($"Login failed: {ex.Message}");
            return;
        }

        builder.UserId = null;
        builder.Password = null;
        builder.Token = conn.Token;
        config.ConnectionString = builder.ConnectionString;
        config.Save();

        Console.WriteLine("Logged in successfully.");
    }

    public static async Task Logout(SdConfig config, ApiClient client)
    {
        if (!string.IsNullOrEmpty(config.ConnectionString))
        {
            var builder = new SmartDataConnectionStringBuilder(config.ConnectionString);
            if (!string.IsNullOrEmpty(builder.Token))
            {
                await using var conn = new SmartDataConnection(builder.ConnectionString);
                try
                {
                    await conn.OpenAsync();
                    await conn.SendAsync("sp_logout", new Dictionary<string, object> { ["Token"] = builder.Token! });
                }
                catch
                {
                }

                builder.Token = null;
                config.ConnectionString = builder.ConnectionString;
                config.Save();
            }
        }

        Console.WriteLine("Logged out.");
    }
}
