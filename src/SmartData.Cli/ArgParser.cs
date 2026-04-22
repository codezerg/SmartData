namespace SmartData.Cli;

public static class ArgParser
{
    public static string? GetFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                return args[i + 1];
        }
        return null;
    }

    public static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    public static List<string> GetAllFlags(string[] args, string flag)
    {
        var values = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                values.Add(args[i + 1]);
        }
        return values;
    }
}
