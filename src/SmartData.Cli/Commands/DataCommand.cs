namespace SmartData.Cli.Commands;

public static class DataCommand
{
    public static async Task Select(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length < 1) { Console.Error.WriteLine("Usage: ds select <table> [--where '{...}'] [--orderby Col[:desc]] [--limit N] [--offset N]"); return; }

        var table = args[0];
        var cmdArgs = new Dictionary<string, object> { ["Table"] = table };

        var where = ArgParser.GetFlag(args, "--where");
        if (where != null) cmdArgs["Where"] = where;

        var orderBy = ArgParser.GetFlag(args, "--orderby");
        if (orderBy != null) cmdArgs["OrderBy"] = orderBy;

        var limit = ArgParser.GetFlag(args, "--limit");
        if (limit != null) cmdArgs["Limit"] = limit;

        var offset = ArgParser.GetFlag(args, "--offset");
        if (offset != null) cmdArgs["Offset"] = offset;

        await client.SendAndPrint("sp_select", cmdArgs);
    }

    public static async Task Insert(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: ds insert <table> --Col1 val1 --Col2 val2"); return; }

        var table = args[0];
        var values = new Dictionary<string, object>();

        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") && i + 1 < args.Length)
            {
                values[args[i][2..]] = args[i + 1];
                i++;
            }
        }

        var valuesJson = System.Text.Json.JsonSerializer.Serialize(values);
        await client.SendAndPrint("sp_insert", new() { ["Table"] = table, ["Values"] = valuesJson });
    }

    public static async Task Update(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: ds update <table> --where '{...}' --set '{...}'"); return; }

        var table = args[0];
        var where = ArgParser.GetFlag(args, "--where");
        var set = ArgParser.GetFlag(args, "--set");

        if (where == null || set == null)
        {
            Console.Error.WriteLine("Both --where and --set are required.");
            return;
        }

        await client.SendAndPrint("sp_update", new() { ["Table"] = table, ["Where"] = where, ["Set"] = set });
    }

    public static async Task Delete(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length < 2) { Console.Error.WriteLine("Usage: ds delete <table> --where '{...}'"); return; }

        var table = args[0];
        var where = ArgParser.GetFlag(args, "--where");

        if (where == null)
        {
            Console.Error.WriteLine("--where is required.");
            return;
        }

        await client.SendAndPrint("sp_delete", new() { ["Table"] = table, ["Where"] = where });
    }
}
