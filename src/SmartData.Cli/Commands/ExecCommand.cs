namespace SmartData.Cli.Commands;

public static class ExecCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: ds exec <sp-name> [--param1 value1 --param2 value2]");
            return;
        }

        var spName = args[0];
        var rest = args[1..];

        // Collect all --key value pairs as parameters
        var parameters = new Dictionary<string, object>();
        for (int i = 0; i < rest.Length; i++)
        {
            if (rest[i].StartsWith("--") && i + 1 < rest.Length)
            {
                var key = rest[i][2..];
                var value = rest[i + 1];
                parameters[key] = value;
                i++;
            }
        }

        await client.SendAndPrint(spName, parameters);
    }
}
