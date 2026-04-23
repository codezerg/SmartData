# SmartData.Client

HTTP client library for calling SmartData's binary RPC endpoint. Modeled after `System.Data.SqlConnection`: a stateful connection driven by a connection string, with explicit `OpenAsync` / `CloseAsync` lifecycle.

- **Target:** .NET 10
- **Dependency:** SmartData.Core (binary serialization, API models)
- **Protocol:** Binary RPC over HTTP POST to `/rpc`
- **Content type:** `application/x-binaryrpc`

## SmartDataConnection

```csharp
await using var conn = new SmartDataConnection(
    "Server=http://localhost:5124;User Id=admin;Password=admin");

await conn.OpenAsync();   // calls sp_login, stores the token

var resp = await conn.SendAsync("usp_customer_list", new Dictionary<string, object>
{
    ["Database"] = "master",
    ["Search"]   = "acme",
});

if (resp.Success)
{
    var data = resp.GetData<CustomerListResult>();
}
```

### Lifecycle

| State | Meaning |
|---|---|
| `Closed` | Initial state. `SendAsync` throws `InvalidOperationException`. `ConnectionString` is mutable. |
| `Connecting` | `OpenAsync` is in flight (calling `sp_login`). |
| `Open` | Token is set; `SendAsync` works. |
| `Broken` | Server returned `Authenticated == false` mid-life (TTL expired or admin revoked). Discard and create a new connection. Server restarts do not break sessions — they're persisted to `_sys_sessions`. |

`CloseAsync` returns the connection to `Closed`. If the token was obtained via `sp_login` (not from a `Token=` connection-string entry), `CloseAsync` calls `sp_logout` best-effort. `Dispose` / `DisposeAsync` are equivalent to `CloseAsync`.

### Constructors

| Signature | Notes |
|---|---|
| `SmartDataConnection()` | Empty connection string; set `ConnectionString` before `OpenAsync`. |
| `SmartDataConnection(string connectionString)` | Most common. |
| `SmartDataConnection(string connectionString, HttpClient httpClient)` | Inject an `HttpClient` (the connection won't dispose it). |

### Properties

| Property | Type | Notes |
|---|---|---|
| `ConnectionString` | `string` | Read-back masks `Password=***`. Setter requires `State == Closed`. |
| `State` | `System.Data.ConnectionState` | `Closed` / `Connecting` / `Open` / `Broken`. |
| `Token` | `string?` | Read-only; populated after `OpenAsync`. |
| `ServerUrl` | `string?` | Normalized server URL. |
| `Timeout` | `TimeSpan` | HTTP timeout. Defaults to the conn-string `Timeout=` (seconds), else 30s. |

### Methods

```csharp
Task OpenAsync(CancellationToken ct = default);
Task CloseAsync();
Task<CommandResponse> SendAsync(
    string command,
    Dictionary<string, object>? args = null,
    CancellationToken ct = default);
```

`SendAsync` throws `InvalidOperationException` if the connection isn't `Open` and `SmartDataException` if the response indicates `Authenticated == false`. HTTP/timeout failures stay surfaceable as `CommandResponse { Success = false, Error = ... }` so callers can distinguish transient from auth failures.

## Connection string keys

| Key | Aliases | Notes |
|---|---|---|
| `Server` | — | Required. Base URL. `http://` is prepended if no scheme. |
| `User Id` | `UID`, `Username`, `User` | Username for password login. |
| `Password` | `PWD` | Password for password login. |
| `Token` | — | Pre-existing token; if set, `OpenAsync` skips `sp_login`. Mutually exclusive with `User Id` / `Password`. |
| `Timeout` | — | HTTP timeout in seconds. Default 30. |

Build conn strings programmatically with `SmartDataConnectionStringBuilder` (a `DbConnectionStringBuilder` subclass that knows the aliases):

```csharp
var b = new SmartDataConnectionStringBuilder
{
    Server = "https://api.example.com",
    Token  = persistedToken,
};
var conn = new SmartDataConnection(b.ConnectionString);
```

## SmartDataException

Thrown for protocol-level failures (`sp_login` rejected, `Authenticated == false` response, malformed payload). Wraps the offending `CommandResponse` when applicable.

```csharp
try
{
    await conn.SendAsync("usp_customer_list");
}
catch (SmartDataException ex)
{
    // ex.Response?.ErrorId, ex.Response?.Error
}
```

## Request / Response (from SmartData.Core)

### CommandRequest

| Property | Type | Description |
|---|---|---|
| `Command` | `string` | Procedure name (e.g. `usp_customer_list`) |
| `Token` | `string?` | Auth token (set by the connection) |
| `Args` | `byte[]?` | Binary-serialized argument dictionary |

### CommandResponse

| Property | Type | Description |
|---|---|---|
| `Success` | `bool` | Whether the call succeeded |
| `Data` | `byte[]?` | Binary-serialized result payload |
| `Error` | `string?` | Error message on failure |
| `ErrorId` | `int?` | User-defined error id (1000+) or system id (0–999) |
| `ErrorSeverity` | `int?` | `ErrorSeverity` enum value |
| `Authenticated` | `bool?` | `null` = no token sent, `true`/`false` = validation result |

Helper methods:
- `GetData<T>()` — deserializes `Data` into typed result (case-insensitive property mapping)
- `GetDataAsJson()` — converts `Data` to JSON string for debugging
- `Ok(object?)` / `Fail(string)` — static factories (server-side)
