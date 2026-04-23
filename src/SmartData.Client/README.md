# SmartData.Client

HTTP client for SmartData's binary RPC protocol.

## Features

- `SmartDataConnection` — `SqlConnection`-style stateful connection with `OpenAsync` / `CloseAsync`
- `SmartDataConnectionStringBuilder` — strongly-typed conn-string keys
- Binary serialization via SmartData.Core

## Usage

```csharp
using SmartData.Client;

await using var conn = new SmartDataConnection(
    "Server=http://localhost:5124;User Id=admin;Password=secret");

await conn.OpenAsync();   // performs sp_login

var resp = await conn.SendAsync("usp_customer_list", new Dictionary<string, object>
{
    ["Database"] = "master",
    ["Search"]   = "acme",
});
```
