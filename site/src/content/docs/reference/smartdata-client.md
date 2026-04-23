---
title: SmartData.Client
description: HTTP client library for the SmartData binary RPC endpoint.
---

HTTP client for the `/rpc` endpoint. `net10.0`. Depends on SmartData.Core. Content type `application/x-binaryrpc`. How-to: [Call procedures from a client](/how-to/call-procedures-from-a-client/).

## SmartDataConnection

Stateful connection modeled on `System.Data.SqlConnection`. Driven by a connection string; lifecycle via `OpenAsync` / `CloseAsync`.

```csharp
await using var conn = new SmartDataConnection(
    "Server=http://localhost:5124;User Id=admin;Password=admin");

await conn.OpenAsync();   // calls sp_login, stores the token

var response = await conn.SendAsync("usp_customer_list", new Dictionary<string, object>
{
    ["Database"] = "smartapp",
    ["Search"]   = "acme",
    ["Page"]     = 1,
});

if (response.Success)
{
    var data = response.GetData<CustomerListResult>();
}
```

### Constructors

| Signature | Description |
|---|---|
| `SmartDataConnection()` | Empty connection string; assign before `OpenAsync`. |
| `SmartDataConnection(string connectionString)` | Most common form. |
| `SmartDataConnection(string connectionString, HttpClient httpClient)` | Inject a custom `HttpClient` (not disposed by the connection). |

### Properties

| Property | Type | Description |
|---|---|---|
| `ConnectionString` | `string` | Read-back masks `Password=***`. Setter requires `State == Closed`. |
| `State` | `System.Data.ConnectionState` | `Closed`, `Connecting`, `Open`, `Broken` (TTL expiry or admin revoke; server restarts do not break sessions). |
| `Token` | `string?` | Read-only; populated by `OpenAsync`. |
| `ServerUrl` | `string?` | Normalized server URL. |
| `Timeout` | `TimeSpan` | HTTP timeout. Defaults to `Timeout=` in seconds, else 30s. |

### Methods

```csharp
Task OpenAsync(CancellationToken ct = default);
Task CloseAsync();
Task<CommandResponse> SendAsync(
    string command,
    Dictionary<string, object>? args = null,
    CancellationToken ct = default);
ValueTask DisposeAsync();
void Dispose();
```

`OpenAsync` calls `sp_login` if the conn string supplies `User Id` + `Password`, or trusts a supplied `Token=`. `SendAsync` throws `InvalidOperationException` if not `Open`, and `SmartDataException` when the server returns `Authenticated == false` (state transitions to `Broken`). HTTP/timeout failures continue to surface as `CommandResponse { Success = false, Error = ... }`.

## SmartDataConnectionStringBuilder

`DbConnectionStringBuilder` subclass with strongly-typed accessors and key aliases.

| Key | Aliases | Notes |
|---|---|---|
| `Server` | — | Required. `http://` prepended if no scheme. |
| `User Id` | `UID`, `Username`, `User` | Username for password login. |
| `Password` | `PWD` | Password for password login. |
| `Token` | — | Pre-existing token; mutually exclusive with `User Id` / `Password`. |
| `Timeout` | — | Seconds. Default 30. |

```csharp
var b = new SmartDataConnectionStringBuilder
{
    Server = "https://api.example.com",
    Token  = persistedToken,
};
var conn = new SmartDataConnection(b.ConnectionString);
```

## SmartDataException

Thrown for protocol-level failures (`sp_login` rejected, `Authenticated == false`, malformed payload). The offending `CommandResponse` is accessible via `ex.Response`.

`CommandRequest` and `CommandResponse` types come from [SmartData.Core](/reference/smartdata-core/#api-models).
