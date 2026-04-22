---
title: SmartData.Client
description: HTTP client library for the SmartData binary RPC endpoint.
---

HTTP client for the `/rpc` endpoint. `net10.0`. Depends on SmartData.Core. Content type `application/x-binaryrpc`. How-to: [Call procedures from a client](/how-to/call-procedures-from-a-client/).

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

`CommandRequest` and `CommandResponse` types come from [SmartData.Core](/reference/smartdata-core/#api-models).
