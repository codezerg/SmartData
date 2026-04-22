using SmartData.Core.Api;
using SmartData.Core.BinarySerialization;

namespace SmartData.Client;

public class SmartDataClient
{
    private readonly HttpClient _http;
    private readonly string _serverUrl;

    public string? Token { get; set; }
    public string? Database { get; set; }

    public SmartDataClient(string serverUrl, HttpClient? httpClient = null)
    {
        _serverUrl = serverUrl.TrimEnd('/');
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<CommandResponse> SendAsync(string command, Dictionary<string, object>? args = null)
    {
        // Database is carried as a regular argument — procedures that target a
        // specific db declare a Database parameter and read it via normal binding.
        if (!string.IsNullOrEmpty(Database))
        {
            args ??= new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (!args.ContainsKey("Database"))
                args["Database"] = Database;
        }

        var request = new CommandRequest
        {
            Command = command,
            Token = Token,
            Args = args != null ? BinarySerializer.Serialize(args) : null
        };

        var requestData = BinarySerializer.Serialize(request);
        var content = new ByteArrayContent(requestData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-binaryrpc");

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync($"{_serverUrl}/rpc", content);
        }
        catch (HttpRequestException)
        {
            return new CommandResponse { Success = false, Error = "Server is unavailable. Please try again later." };
        }
        catch (TaskCanceledException)
        {
            return new CommandResponse { Success = false, Error = "Request timed out. Please try again later." };
        }

        var responseData = await response.Content.ReadAsByteArrayAsync();

        return BinarySerializer.Deserialize<CommandResponse>(responseData)
            ?? new CommandResponse { Success = false, Error = "No response from server." };
    }
}
