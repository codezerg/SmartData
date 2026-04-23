---
title: Binary RPC
description: One endpoint, one binary protocol — how SmartData speaks over HTTP.
---

Every client-server call in SmartData is a `POST /rpc` carrying a binary body. One endpoint, two message types, no per-procedure routing. `POST /rpc` is wired by a single line in `Program.cs`:

```csharp
app.UseSmartData();
// maps POST /rpc + GET /health
```

## Wire format at a glance

- **Content type:** `application/x-binaryrpc`
- **Serializer:** `BinarySerializer` from `SmartData.Core`, used for both directions
- **Property matching:** by name, case-insensitive
- **Args double-serialized:** `CommandRequest.Args` is itself a binary-serialized dictionary inside the outer binary envelope

## Request / response shapes

```csharp
public class CommandRequest
{
    public string  Command  { get; set; } = "";   // "usp_customer_list"
    public string? Token    { get; set; }         // session token
    public byte[]? Args     { get; set; }         // binary-serialized args dictionary
                                                  //   (includes Database = "master" when targeting a db)
}

public class CommandResponse
{
    public bool    Success       { get; set; }
    public byte[]? Data          { get; set; }    // binary-serialized result
    public string? Error         { get; set; }
    public int?    ErrorId       { get; set; }    // 0–999 system, 1000+ user
    public int?    ErrorSeverity { get; set; }    // 0=Error, 1=Severe, 2=Fatal
    public bool?   Authenticated { get; set; }

    public T?      GetData<T>();                  // deserialize Data into T
    public string? GetDataAsJson();               // debug helper
}
```

`ErrorId` and `ErrorSeverity` on the response let remote clients react programmatically without string-matching messages. See [Procedures → Errors](/fundamentals/procedures/#errors) for where these come from.

## Call flow

```
Client                               Server
  │                                    │
  │  POST /rpc                         │
  │  Body: BinarySerialize(            │
  │    CommandRequest {                │
  │      Command = "usp_...",          │
  │      Token   = "...",              │
  │      Args    = BinarySerialize({   │
  │        Database = "master",        │
  │        key = value, ...            │
  │      })                            │
  │    })                              │
  │ ─────────────────────────────────> │
  │                                    │  CommandRouter.RouteAsync
  │                                    │   ├─ Deserialize args
  │                                    │   ├─ Validate token
  │                                    │   └─ ProcedureExecutor
  │                                    │        ├─ Resolve from catalog
  │                                    │        ├─ Open DI scope
  │                                    │        ├─ Instantiate procedure
  │                                    │        ├─ Auth/permission check
  │                                    │        ├─ Bind args to properties
  │                                    │        └─ Execute / ExecuteAsync
  │                                    │
  │  BinarySerialize(                  │
  │    CommandResponse { ... })        │
  │ <───────────────────────────────── │
```

## Why not REST or gRPC

The binary-over-one-endpoint design is compact and easy to stand up — but it buys that by giving up things you get free elsewhere:

- **No HTTP caching.** Every call is `POST /rpc`; `ETag`, `Cache-Control`, CDN edges, `304 Not Modified` don't apply.
- **Opaque to browser devtools.** The Network tab shows a binary blob, not JSON. Use `CommandResponse.GetDataAsJson()` server-side when debugging.
- **No curl testing.** You can't poke at procedures with a one-liner. Call `IProcedureService` from a test harness or spin up the admin console.
- **Middleware sees one route.** Rate limiting, per-endpoint logging, OpenAPI generators — anything that keys on route doesn't differentiate between procedures without custom code that reads the body.

Fine trade-offs for typed C#-to-C# apps. Worth knowing if you're comparing against REST or gRPC for a mixed-language stack.

## Endpoints

| Endpoint | Method | Purpose |
| --- | --- | --- |
| `/rpc` | `POST` | All procedure calls. Binary envelope in, binary envelope out. |
| `/health` | `GET` | JSON status + diagnostics. Safe for load-balancer probes. |

That's it. There is no `/rpc/usp_customer_list` form — the procedure name lives in the body.

## Error surface

| Thrown exception | What the caller sees |
| --- | --- |
| `ProcedureException` (from `RaiseError`) | `Success = false`, full message, `ErrorId`, `ErrorSeverity` |
| `UnauthorizedAccessException` | Generic message unless `IncludeExceptionDetails = true` |
| Anything else | Generic "internal error" unless `IncludeExceptionDetails = true` |

**Do not** turn on `IncludeExceptionDetails` in production — you'll leak stack traces to remote clients.

## The client side

`SmartDataConnection` wraps all of this:

```csharp
await using var conn = new SmartDataConnection(
    "Server=http://localhost:5124;User Id=admin;Password=secret");

await conn.OpenAsync();   // performs sp_login

var response = await conn.SendAsync("usp_customer_list",
    new Dictionary<string, object>
    {
        ["Database"] = "master",
        ["Search"]   = "acme",
        ["Page"]     = 1,
        ["PageSize"] = 20
    });

if (response.Success)
    HandleList(response.GetData<CustomerListResult>());
else
    ShowError(response.Error);
```

See [Call procedures from a client](/how-to/call-procedures-from-a-client/) for a full example, and the [SmartData.Client reference](/reference/smartdata-client/) for the full surface.

## Related

- [Procedures](/fundamentals/procedures/) — what lives on the other side of `/rpc`
- [Call procedures from a client](/how-to/call-procedures-from-a-client/) — step-by-step
- [SmartData.Core reference](/reference/smartdata-core/) — the binary serializer
