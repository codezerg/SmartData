using System.Text.Json;

namespace SmartData.Cli.Commands;

public static class ExportImportCommand
{
    public static async Task Export(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length < 1) { Console.Error.WriteLine("Usage: sd export <table> [--where '{...}'] [--out file.json]"); return; }

        var table = args[0];
        var outFile = ArgParser.GetFlag(args, "--out") ?? $"{table}.json";

        var spArgs = new Dictionary<string, object> { ["Table"] = table };

        var where = ArgParser.GetFlag(args, "--where");
        if (where != null) spArgs["Where"] = where;

        var result = await client.SendAsync("sp_data_export", spArgs);

        if (!result.Success)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return;
        }

        var data = result.GetData<ExportResult>();
        if (data?.rows == null)
        {
            Console.Error.WriteLine("No data returned.");
            return;
        }

        var json = JsonSerializer.Serialize(data.rows, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outFile, json);
        Console.WriteLine($"Exported {data.count} rows to {outFile}");
    }

    public static async Task Import(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length < 1) { Console.Error.WriteLine("Usage: sd import <table> --file data.json"); return; }

        var table = args[0];
        var file = ArgParser.GetFlag(args, "--file") ?? $"{table}.json";

        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"File not found: {file}");
            return;
        }

        var json = await File.ReadAllTextAsync(file);

        await client.SendAndPrint("sp_data_import", new()
        {
            ["Table"] = table,
            ["Rows"] = json
        });
    }

    private class ExportResult
    {
        public string? table { get; set; }
        public int count { get; set; }
        public List<Dictionary<string, object?>>? rows { get; set; }
    }
}
