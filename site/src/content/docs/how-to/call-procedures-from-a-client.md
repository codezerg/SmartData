---
title: Call procedures from a client
description: Use SmartData.Client to invoke procedures over binary RPC.
---

`SmartDataClient` wraps the binary RPC protocol. Construct one, set `Database` and `Token`, call `SendAsync`.

## Install

```bash
dotnet add package SmartData.Client
```

## Minimal call

```csharp
using SmartData.Client;

var client = new SmartDataClient("http://localhost:5124");
client.Database = "master";
client.Token    = sessionToken;     // after login

var response = await client.SendAsync("usp_customer_list",
    new Dictionary<string, object>
    {
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

## Login first

Authentication is a procedure call too — there's no separate auth endpoint:

```csharp
var loginResp = await client.SendAsync("sp_login",
    new Dictionary<string, object>
    {
        ["Username"] = "admin",
        ["Password"] = secret,
    });

if (!loginResp.Success)
    throw new InvalidOperationException(loginResp.Error);

var login = loginResp.GetData<LoginResult>();
client.Token = login.Token;      // reuse across subsequent calls
```

The client treats `Token` as opaque — store it in whatever session/cookie mechanism your app uses.

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

`ErrorId` + `ErrorSeverity` let clients switch on failure types without matching strings.

## HttpClient lifetime

`SmartDataClient` holds an internal `HttpClient`. For short-lived tools just new one up. For a long-running app, treat it as a singleton — reuse the same client, change `Token` / `Database` per call as needed.

## Related

- [Binary RPC](/fundamentals/binary-rpc/) — the wire protocol
- [Return DTOs, not entities](/how-to/return-dtos-not-entities/) — why the result types are shaped the way they are
- [SmartData.Client reference](/reference/smartdata-client/) — full surface
