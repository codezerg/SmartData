namespace SmartData.Cli.Commands;

public static class LoginCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
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

        var result = await client.SendAsync("sp_login", new Dictionary<string, object>
        {
            ["Username"] = username,
            ["Password"] = password
        });

        if (!result.Success)
        {
            Console.Error.WriteLine($"Login failed: {result.Error}");
            return;
        }

        var data = result.GetData<Dictionary<string, object>>();
        if (data != null && data.TryGetValue("Token", out var tokenObj))
        {
            config.Token = tokenObj?.ToString();
            config.Save();
            Console.WriteLine("Logged in successfully.");
        }
    }

    public static async Task Logout(SdConfig config, ApiClient client)
    {
        if (!string.IsNullOrEmpty(config.Token))
        {
            await client.SendAsync("sp_logout", new Dictionary<string, object> { ["Token"] = config.Token });
        }

        config.Token = null;
        config.Save();
        Console.WriteLine("Logged out.");
    }
}
