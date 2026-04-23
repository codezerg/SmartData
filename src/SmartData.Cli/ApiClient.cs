using SmartData.Client;
using SmartData.Core.Api;

namespace SmartData.Cli;

public class ApiClient
{
    private readonly SdConfig _config;
    private SmartDataConnection? _connection;
    private bool _opened;

    public ApiClient(SdConfig config)
    {
        _config = config;
    }

    public async Task<CommandResponse> SendAsync(string command, Dictionary<string, object>? args = null)
    {
        var conn = EnsureConnection();

        if (!_opened)
        {
            try
            {
                await conn.OpenAsync();
                _opened = true;
            }
            catch (SmartDataException ex)
            {
                Console.Error.WriteLine($"Connection failed: {ex.Message}");
                Environment.Exit(1);
            }
        }

        if (!string.IsNullOrEmpty(_config.Database))
        {
            args ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!args.ContainsKey("Database"))
                args["Database"] = _config.Database;
        }

        try
        {
            return await conn.SendAsync(command, args);
        }
        catch (SmartDataException)
        {
            Console.Error.WriteLine("Session expired. Run: sd login");
            Environment.Exit(1);
            return CommandResponse.Fail("unreachable");
        }
    }

    public async Task<CommandResponse> SendAndPrint(string command, Dictionary<string, object>? args = null)
    {
        var result = await SendAsync(command, args);

        if (!result.Success)
        {
            Console.Error.WriteLine($"Error: {result.Error}");
            return result;
        }

        var json = result.GetDataAsJson();
        if (json != null)
            Console.WriteLine(json);

        return result;
    }

    private SmartDataConnection EnsureConnection()
    {
        if (_connection != null)
            return _connection;

        if (string.IsNullOrEmpty(_config.ConnectionString))
        {
            Console.Error.WriteLine("Not connected. Run: sd connect <server>");
            Environment.Exit(1);
        }

        var builder = new SmartDataConnectionStringBuilder(_config.ConnectionString);
        if (string.IsNullOrEmpty(builder.Token) && string.IsNullOrEmpty(builder.UserId))
        {
            Console.Error.WriteLine("Not authenticated. Run: sd login");
            Environment.Exit(1);
        }

        _connection = new SmartDataConnection(_config.ConnectionString!);
        return _connection;
    }
}
