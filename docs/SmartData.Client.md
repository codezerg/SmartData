# SmartData.Client

HTTP client library for calling SmartData's binary RPC endpoint. Used by the Blazor frontend to communicate with the backend API.

- **Target:** .NET 10
- **Dependency:** SmartData.Core (binary serialization, API models)
- **Protocol:** Binary RPC over HTTP POST to `/rpc`
- **Content type:** `application/x-binaryrpc`

## SmartDataClient

Single class in the project. Manages server connection, authentication token, and database context.

```csharp
var client = new SmartDataClient("http://localhost:5124");
client.Token = "...";      // set after login
client.Database = "smartapp"; // target database

var response = await client.SendAsync("sp_customer_list", new Dictionary<string, object>
{
    ["search"] = "acme",
    ["page"] = 1
});

if (response.Success)
{
    var data = response.GetData<CustomerListResult>();
}
```

### Constructor

| Parameter | Type | Description |
|-----------|------|-------------|
| `serverUrl` | `string` | Base URL of the SmartData backend (trailing slash trimmed) |
| `httpClient` | `HttpClient?` | Optional custom HttpClient; creates a new one if omitted |

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Token` | `string?` | Auth token, sent with every request |
| `Database` | `string?` | Target database name |

### SendAsync

```csharp
Task<CommandResponse> SendAsync(string command, Dictionary<string, object>? args = null)
```

Serializes a `CommandRequest` (command name + token + database + args) via `BinarySerializer`, POSTs to `{serverUrl}/rpc`, and deserializes the `CommandResponse`.

## Request / Response (from SmartData.Core)

### CommandRequest

| Property | Type | Description |
|----------|------|-------------|
| `Command` | `string` | Procedure name (e.g. `sp_customer_list`) |
| `Token` | `string?` | Auth token |
| `Database` | `string?` | Target database |
| `Args` | `byte[]?` | Binary-serialized argument dictionary |

### CommandResponse

| Property | Type | Description |
|----------|------|-------------|
| `Success` | `bool` | Whether the call succeeded |
| `Data` | `byte[]?` | Binary-serialized result payload |
| `Error` | `string?` | Error message on failure |

Helper methods:
- `GetData<T>()` — deserializes `Data` into typed result
- `GetDataAsJson()` — converts `Data` to JSON string
- `Ok(object?)` / `Fail(string)` — static factories (server-side)

## Frontend Integration

`DataService` in `SmartApp.Frontend/Services/` wraps `SmartDataClient` and provides convenience methods:

| Method | Returns | Description |
|--------|---------|-------------|
| `CallAsync(procedure, args?)` | `Dictionary<string, object?>` | Untyped dictionary result |
| `CallAsync<T>(procedure, args?)` | `T?` | Typed deserialization |
| `CallListAsync(procedure, args?)` | `List<Dictionary<string, object?>>` | List of dictionaries |
| `CallJsonAsync(procedure, args?)` | `string?` | Raw JSON string |

`DataService` auto-initializes on first call: logs in, creates the database if needed, and seeds data. Args can be passed as a `Dictionary<string, object>` or any object (properties reflected to dictionary).
