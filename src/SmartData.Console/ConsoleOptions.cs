namespace SmartData.Console;

public class ConsoleOptions
{
    /// <summary>
    /// Allow the console to be served in non-Development environments.
    /// Default: false (console is disabled outside Development).
    /// </summary>
    public bool AllowInProduction { get; set; }

    /// <summary>
    /// URL prefix for console routes (without leading/trailing slashes).
    /// Default: "console" → routes at /console/...
    /// </summary>
    public string RoutePrefix { get; set; } = "console";

}
