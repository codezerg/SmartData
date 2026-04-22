using SmartData.Client;
using SmartData.Core.Api;

namespace SmartData.Cli;

public class ApiClient
{
    private readonly SmartDataClient? _client;
    private readonly SdConfig _config;

    public ApiClient(SdConfig config)
    {
        _config = config;
        if (!string.IsNullOrEmpty(config.Server))
        {
            _client = new SmartDataClient(config.Server)
            {
                Token = config.Token,
                Database = config.Database
            };
        }
    }

    public async Task<CommandResponse> SendAsync(string command, Dictionary<string, object>? args = null)
    {
        if (_client == null)
        {
            Console.Error.WriteLine("Not connected. Run: sd connect <server>");
            Environment.Exit(1);
        }

        _client.Token = _config.Token;
        _client.Database = _config.Database;
        return await _client.SendAsync(command, args);
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
}
