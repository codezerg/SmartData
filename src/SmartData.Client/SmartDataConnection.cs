using System.Data;
using System.Net.Http.Headers;
using SmartData.Core.Api;
using SmartData.Core.BinarySerialization;

namespace SmartData.Client;

public sealed class SmartDataConnection : IAsyncDisposable, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    private string _connectionString = string.Empty;
    private SmartDataConnectionStringBuilder _builder = new();
    private string? _token;
    private bool _tokenFromLogin;
    private ConnectionState _state = ConnectionState.Closed;
    private TimeSpan _timeout = TimeSpan.FromSeconds(30);

    public SmartDataConnection() : this(string.Empty, null) { }

    public SmartDataConnection(string connectionString) : this(connectionString, null) { }

    public SmartDataConnection(string connectionString, HttpClient? httpClient)
    {
        _ownsHttp = httpClient is null;
        _http = httpClient ?? new HttpClient();
        ConnectionString = connectionString;
    }

    public string ConnectionString
    {
        get
        {
            if (string.IsNullOrEmpty(_builder.Password))
                return _builder.ConnectionString;

            var masked = new SmartDataConnectionStringBuilder(_builder.ConnectionString)
            {
                Password = "***"
            };
            return masked.ConnectionString;
        }
        set
        {
            if (_state != ConnectionState.Closed)
                throw new InvalidOperationException("Cannot change ConnectionString while the connection is not Closed.");

            _connectionString = value ?? string.Empty;
            _builder = new SmartDataConnectionStringBuilder(_connectionString);
            _timeout = TimeSpan.FromSeconds(_builder.Timeout);
            _http.Timeout = _timeout;
        }
    }

    public ConnectionState State => _state;

    public string? Token => _token;

    public string? ServerUrl => NormalizeServerUrl(_builder.Server);

    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            _timeout = value;
            _http.Timeout = value;
        }
    }

    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (_state != ConnectionState.Closed)
            throw new InvalidOperationException($"Connection is already {_state}.");

        var server = NormalizeServerUrl(_builder.Server)
            ?? throw new InvalidOperationException("Connection string requires Server.");

        var hasToken = !string.IsNullOrEmpty(_builder.Token);
        var hasCreds = !string.IsNullOrEmpty(_builder.UserId) && !string.IsNullOrEmpty(_builder.Password);

        if (hasToken && (hasCreds || !string.IsNullOrEmpty(_builder.UserId) || !string.IsNullOrEmpty(_builder.Password)))
            throw new InvalidOperationException("Connection string cannot contain both Token and User Id/Password.");

        if (!hasToken && !hasCreds)
            throw new InvalidOperationException("Connection string requires either Token or User Id + Password.");

        _state = ConnectionState.Connecting;

        try
        {
            if (hasToken)
            {
                _token = _builder.Token;
                _tokenFromLogin = false;
            }
            else
            {
                var response = await SendRawAsync("sp_login", new Dictionary<string, object>
                {
                    ["Username"] = _builder.UserId!,
                    ["Password"] = _builder.Password!,
                }, token: null, ct);

                if (!response.Success)
                    throw new SmartDataException(response.Error ?? "Login failed.", response);

                var data = response.GetData<Dictionary<string, object>>();
                if (data == null || !data.TryGetValue("Token", out var tokenObj) || tokenObj is not string token || string.IsNullOrEmpty(token))
                    throw new SmartDataException("Login response did not contain a token.", response);

                _token = token;
                _tokenFromLogin = true;
            }

            _state = ConnectionState.Open;
        }
        catch
        {
            _token = null;
            _tokenFromLogin = false;
            _state = ConnectionState.Closed;
            throw;
        }
    }

    public async Task CloseAsync()
    {
        if (_state == ConnectionState.Closed)
            return;

        var token = _token;
        var shouldLogout = _tokenFromLogin && !string.IsNullOrEmpty(token) && _state == ConnectionState.Open;

        _token = null;
        _tokenFromLogin = false;
        _state = ConnectionState.Closed;

        if (shouldLogout)
        {
            try
            {
                await SendRawAsync("sp_logout", new Dictionary<string, object> { ["Token"] = token! }, token, CancellationToken.None);
            }
            catch
            {
            }
        }
    }

    public async Task<CommandResponse> SendAsync(string command, Dictionary<string, object>? args = null, CancellationToken ct = default)
    {
        if (_state != ConnectionState.Open)
            throw new InvalidOperationException("Connection is not open.");

        var response = await SendRawAsync(command, args, _token, ct);

        if (response.Authenticated == false)
        {
            _state = ConnectionState.Broken;
            throw new SmartDataException("Session is no longer valid.", response);
        }

        return response;
    }

    private async Task<CommandResponse> SendRawAsync(string command, Dictionary<string, object>? args, string? token, CancellationToken ct)
    {
        var server = NormalizeServerUrl(_builder.Server)
            ?? throw new InvalidOperationException("Connection string requires Server.");

        var request = new CommandRequest
        {
            Command = command,
            Token = token,
            Args = args != null ? BinarySerializer.Serialize(args) : null,
        };

        var requestData = BinarySerializer.Serialize(request);
        var content = new ByteArrayContent(requestData);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-binaryrpc");

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _http.PostAsync($"{server}/rpc", content, ct);
        }
        catch (HttpRequestException)
        {
            return new CommandResponse { Success = false, Error = "Server is unavailable. Please try again later." };
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CommandResponse { Success = false, Error = "Request timed out. Please try again later." };
        }

        var responseData = await httpResponse.Content.ReadAsByteArrayAsync(ct);

        return BinarySerializer.Deserialize<CommandResponse>(responseData)
            ?? new CommandResponse { Success = false, Error = "No response from server." };
    }

    private static string? NormalizeServerUrl(string? server)
    {
        if (string.IsNullOrWhiteSpace(server))
            return null;

        var url = server.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "http://" + url;
        return url.TrimEnd('/');
    }

    public async ValueTask DisposeAsync()
    {
        try { await CloseAsync(); } catch { }
        if (_ownsHttp) _http.Dispose();
    }

    public void Dispose()
    {
        try { CloseAsync().GetAwaiter().GetResult(); } catch { }
        if (_ownsHttp) _http.Dispose();
    }
}
