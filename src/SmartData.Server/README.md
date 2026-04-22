# SmartData.Server

Server engine for the SmartData framework — AutoRepo ORM, stored procedure framework, and RPC endpoint.

## Features

- AutoRepo ORM with automatic schema migration from entity classes
- Stored procedure framework with assembly scanning and dependency injection
- Binary RPC endpoint (`/rpc`) for client-server communication
- Provider interfaces for pluggable database backends
- Built-in system procedures for database management

## Usage

```csharp
builder.Services.AddSmartData(options =>
{
    // configure options
});
```

Pair with a database provider package:
- `SmartData.Server.Sqlite` for SQLite
- `SmartData.Server.SqlServer` for SQL Server
