# SmartData.Client

HTTP client for SmartData's binary RPC protocol.

## Features

- `SmartDataClient` for making typed RPC calls over a single `/rpc` endpoint
- Binary serialization/deserialization via SmartData.Core
- Typed `CallAsync<T>`, `CallListAsync`, and `CallJsonAsync` methods

## Usage

```csharp
services.AddSmartDataClient(options =>
{
    options.BaseUrl = "http://localhost:5124";
});
```
