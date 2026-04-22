---
title: Your first RPC call
description: Wire up SmartData.Client and invoke the procedure from your-first-procedure over binary RPC.
---

Same procedure as [the previous page](/get-started/your-first-procedure/) — `usp_customer_list` — now called from a separate console process over HTTP. The only functional changes are (a) logging in, and (b) `SmartDataClient.SendAsync` in place of `IProcedureService.ExecuteAsync`. Everything else stays the same.

Prereqs:

- [Your first procedure](/get-started/your-first-procedure/) complete — the `HelloSmartData` server is what we'll call.
- Server running. `dotnet run` it and note the port Kestrel prints (e.g. `Now listening on: http://localhost:5219`).

## 1. Why auth now matters

The previous page used `IProcedureService` — framework authority, auth gate bypassed, `UserId = "system"`. `POST /rpc` is wired to `IAuthenticatedProcedureService` instead, so every incoming call needs a valid session token. We'll log in once with the built-in `admin`/`admin` account and reuse the token.

No change to the server is required. See [Procedures → Two callers, one boundary](/fundamentals/procedures/#two-callers-one-boundary) for the trust split.

## 2. Create the client project

```bash
cd ..
dotnet new console -n HelloSmartData.Demo
cd HelloSmartData.Demo

dotnet add package SmartData.Client
dotnet add package SmartData.Contracts   # for LoginResult
```

## 3. Mirror the result DTO

`GetData<T>()` deserializes by property name, case-insensitive — the client-side type doesn't have to be the same class or even in the same namespace as the server's `CustomerListResult`. Minimal mirror at the bottom of `Program.cs`:

```csharp
public class CustomerListResult
{
    public List<Customer> Items { get; set; } = new();
    public int            Total { get; set; }
}

public class Customer
{
    public int     Id          { get; set; }
    public string  CompanyName { get; set; } = "";
    public string? City        { get; set; }
}
```

See [Return DTOs, not entities](/how-to/return-dtos-not-entities/) for why production code should return a narrower DTO instead of the entity.

## 4. Program.cs — log in, then call

Replace `Program.cs` (substitute `<port>` with the port the server is listening on):

```csharp
using SmartData.Client;
using SmartData.Contracts;     // LoginResult

var client = new SmartDataClient("http://localhost:<port>");

// 1. Log in (admin:admin is the default on the master database)
var loginResp = await client.SendAsync("sp_login", new()
{
    ["Username"] = "admin",
    ["Password"] = "admin",
});
if (!loginResp.Success)
    throw new Exception($"Login failed: {loginResp.Error}");

var login = loginResp.GetData<LoginResult>()!;
client.Token    = login.Token;
client.Database = "master";

// 2. Call the procedure
var listResp = await client.SendAsync("usp_customer_list", new()
{
    ["Search"] = "acme",
});
if (!listResp.Success)
    throw new Exception($"{listResp.Error} (id={listResp.ErrorId})");

var result = listResp.GetData<CustomerListResult>()!;
foreach (var c in result.Items)
    Console.WriteLine($"{c.Id,3}  {c.CompanyName,-12}  {c.City}");
```

Five things worth naming:

1. `new()` is a target-typed `Dictionary<string, object>` — `SendAsync` takes the dictionary as the args bag.
2. **Login is a procedure, not an endpoint.** `sp_login` is one more RPC call, no special route.
3. `Token` and `Database` are plain settable properties — set once after login, reuse for every subsequent call.
4. Every response carries `Success` / `Error` / `ErrorId` / `ErrorSeverity` — switch on those rather than parsing message strings. Details in [Binary RPC](/fundamentals/binary-rpc/).
5. Browser devtools can't inspect `/rpc` — it's binary. Use `response.GetDataAsJson()` server-side when debugging.

Append the DTO classes from step 3 and save.

## 5. Run both

```bash
# terminal 1 — the server from the previous page
cd ../HelloSmartData
dotnet run

# terminal 2 — this client
cd ../HelloSmartData.Demo
dotnet run
```

Expected output:

```
  1  Acme Corp     Springfield
  3  Acme Labs     Portland
```

Same two rows the previous page returned from `/demo`, now fetched over HTTP.

## What just happened

- Client binary-serialized a `CommandRequest { Command = "usp_customer_list", Token, Args }` — the `Args` dictionary itself is binary-serialized and holds `Database = "master"` alongside `Search = "acme"` — then POSTed it to `/rpc`.
- Server's `CommandRouter` deserialized, validated the token, and handed off to `ProcedureExecutor`.
- `ProcedureExecutor` opened a DI scope, instantiated `CustomerList`, bound `Search` by name, called `Execute`.
- Result binary-serialized into `CommandResponse.Data`; `GetData<CustomerListResult>()` deserialized it on this end.

Full picture: [Architecture → Request lifecycle](/overview/architecture/#request-lifecycle).

## Where to go next

- **Four-procedure CRUD.** [Build a CRUD app](/tutorials/build-a-crud-app/) extends this with save, delete, and DTO folder layout.
- **Mental model.** [Binary RPC](/fundamentals/binary-rpc/) — wire format, call flow, error fields.
- **API surface.** [SmartData.Client reference](/reference/smartdata-client/) — the full `SmartDataClient` surface.
