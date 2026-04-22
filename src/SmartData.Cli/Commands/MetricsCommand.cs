namespace SmartData.Cli.Commands;

public static class MetricsCommand
{
    public static async Task Run(string[] args, SdConfig config, ApiClient client)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        var sub = args[0].ToLowerInvariant();
        var rest = args[1..];

        switch (sub)
        {
            case "list":
                await List(rest, client);
                break;
            case "watch":
                await Watch(rest, client);
                break;
            case "traces":
                await Traces(rest, client);
                break;
            case "trace":
                await TraceGet(rest, client);
                break;
            case "exceptions":
                await Exceptions(rest, client);
                break;
            default:
                Console.Error.WriteLine($"Unknown metrics command: {sub}");
                PrintUsage();
                break;
        }
    }

    private static async Task List(string[] args, ApiClient client)
    {
        var parameters = new Dictionary<string, object>();

        var name = ArgParser.GetFlag(args, "--name");
        if (name != null) parameters["Name"] = name;

        var type = ArgParser.GetFlag(args, "--type");
        if (type != null) parameters["Type"] = type;

        var source = ArgParser.GetFlag(args, "--source");
        if (source != null) parameters["Source"] = source;

        var limit = ArgParser.GetFlag(args, "--limit");
        if (limit != null) parameters["PageSize"] = limit;

        await client.SendAndPrint("sp_metrics", parameters);
    }

    private static async Task Watch(string[] args, ApiClient client)
    {
        var intervalStr = ArgParser.GetFlag(args, "--interval") ?? "5";
        if (!int.TryParse(intervalStr, out var interval) || interval < 1)
            interval = 5;

        var name = ArgParser.GetFlag(args, "--name");

        Console.WriteLine($"Watching metrics every {interval}s (Ctrl+C to stop)...");
        Console.WriteLine();

        while (true)
        {
            var parameters = new Dictionary<string, object> { ["Source"] = "live", ["PageSize"] = "200" };
            if (name != null) parameters["Name"] = name;

            Console.Clear();
            Console.WriteLine($"=== Metrics (live, {DateTime.Now:HH:mm:ss}) ===");
            Console.WriteLine();
            await client.SendAndPrint("sp_metrics", parameters);

            await Task.Delay(interval * 1000);
        }
    }

    private static async Task Traces(string[] args, ApiClient client)
    {
        var parameters = new Dictionary<string, object>();

        var procedure = ArgParser.GetFlag(args, "--procedure");
        if (procedure != null) parameters["Procedure"] = procedure;

        if (ArgParser.HasFlag(args, "--errors"))
            parameters["ErrorsOnly"] = "true";

        var minDuration = ArgParser.GetFlag(args, "--min-duration");
        if (minDuration != null) parameters["MinDurationMs"] = minDuration;

        var source = ArgParser.GetFlag(args, "--source");
        if (source != null) parameters["Source"] = source;

        var limit = ArgParser.GetFlag(args, "--limit");
        if (limit != null) parameters["PageSize"] = limit;

        await client.SendAndPrint("sp_traces", parameters);
    }

    private static async Task TraceGet(string[] args, ApiClient client)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: sd metrics trace <traceId>");
            return;
        }

        await client.SendAndPrint("sp_traces", new() { ["TraceId"] = args[0] });
    }

    private static async Task Exceptions(string[] args, ApiClient client)
    {
        var parameters = new Dictionary<string, object>();

        var type = ArgParser.GetFlag(args, "--type");
        if (type != null) parameters["ExceptionType"] = type;

        var procedure = ArgParser.GetFlag(args, "--procedure");
        if (procedure != null) parameters["Procedure"] = procedure;

        var source = ArgParser.GetFlag(args, "--source");
        if (source != null) parameters["Source"] = source;

        var limit = ArgParser.GetFlag(args, "--limit");
        if (limit != null) parameters["PageSize"] = limit;

        await client.SendAndPrint("sp_exceptions", parameters);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: sd metrics <command> [args]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  list [--name pattern] [--type counter|histogram|gauge] [--source live|db]");
        Console.WriteLine("  watch [--interval 5] [--name pattern]        Live-refresh metrics");
        Console.WriteLine("  traces [--procedure X] [--errors] [--min-duration 500]");
        Console.WriteLine("  trace <traceId>                              Show full span tree");
        Console.WriteLine("  exceptions [--type X] [--procedure Y]        List exceptions");
    }
}
