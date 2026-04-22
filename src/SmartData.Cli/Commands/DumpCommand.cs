namespace SmartData.Cli.Commands;

public static class DumpCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        var outFile = ArgParser.GetFlag(args, "--out");

        var result = await client.SendAsync("sp_dump");

        if (!result.Success)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return;
        }

        var data = result.GetData<Dictionary<string, object>>();
        if (data != null && data.TryGetValue("markdown", out var mdObj))
        {
            var markdown = mdObj?.ToString() ?? "";

            if (outFile != null)
            {
                await File.WriteAllTextAsync(outFile, markdown);
                Console.WriteLine($"Dump saved to {outFile}");
            }
            else
            {
                Console.WriteLine(markdown);
            }
        }
    }
}
