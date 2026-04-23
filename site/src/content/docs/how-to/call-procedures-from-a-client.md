---
title: Call procedures from a client
description: Use SmartData.Client to invoke procedures over binary RPC.
---

`SmartDataConnection` wraps the binary RPC protocol. Build it with a connection string, `OpenAsync` once, then `SendAsync` per call.

## Install

```bash
dotnet add package SmartData.Client
```

## Minimal call

```csharp
using SmartData.Client;

await using var conn = new SmartDataConnection(
    "Server=http://localhost:5124;User Id=admin;Password=secret");

await conn.OpenAsync();   // performs sp_login, stores the token

var response = await conn.SendAsync("usp_customer_list",
    new Dictionary<string, object>
    {
        ["Database"] = "master",
        ["Search"]   = "acme",
        ["Page"]     = 1,
        ["PageSize"] = 20,
    });

if (response.Success)
{
    var result = response.GetData<CustomerListResult>();
    foreach (var item in result.Items)
        Console.WriteLine($"{item.Id} {item.CompanyName}");
}
else
{
    Console.Error.WriteLine($"Error: {response.Error} (id={response.ErrorId}, severity={response.ErrorSeverity})");
}
```

## Reusing a token

When you've already logged in elsewhere (e.g. a CLI persisted the token to disk), open with `Token=` instead of credentials. `OpenAsync` skips `sp_login`:

```csharp
await using var conn = new SmartDataConnection(
    $"Server=http://localhost:5124;Token={persistedToken}");

await conn.OpenAsync();
// SendAsync will surface SmartDataException if the token has expired.
```

`Token` and `User Id`/`Password` are mutually exclusive — supplying both throws on `OpenAsync`.

## DTOs — keep them matching by name

`GetData<T>()` deserialises by property name, case-insensitive. Your client-side `CustomerListResult` and `CustomerItem` need matching property names with the server's contracts — they don't need to be the *same type* or even in the same namespace.

Minimal client-side contract:

```csharp
public class CustomerListResult
{
    public List<CustomerItem> Items { get; set; } = new();
    public int Total    { get; set; }
    public int Page     { get; set; }
    public int PageSize { get; set; }
}

public class CustomerItem
{
    public int    Id          { get; set; }
    public string CompanyName { get; set; } = "";
    public string Industry    { get; set; } = "";
}
```

## Error handling

`CommandResponse` carries the server's `ProcedureException` details:

| Field | Meaning |
| --- | --- |
| `Success` | `true` if the procedure returned; `false` if it threw |
| `Error` | Message — from `RaiseError` when `Success` is `false` |
| `ErrorId` | Integer message id (0–999 system, 1000+ user) |
| `ErrorSeverity` | 0 = Error, 1 = Severe, 2 = Fatal |
| `Authenticated` | Whether the token was valid |

When `Authenticated == false`, `SendAsync` throws `SmartDataException` and the connection transitions to `Broken`. Open a new connection to recover.

## Connection lifetime

`SmartDataConnection` is `IAsyncDisposable`. For a long-running app, hold one open connection and share it; for short-lived tools, `await using` ensures `sp_logout` runs on dispose. The setter on `ConnectionString` requires `State == Closed` — open one connection per credential pair.

## Related

- [Binary RPC](/fundamentals/binary-rpc/) — the wire protocol
- [Return DTOs, not entities](/how-to/return-dtos-not-entities/) — why the result types are shaped the way they are
- [SmartData.Client reference](/reference/smartdata-client/) — full surface
