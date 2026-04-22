# SmartData.Server.SqlServer

SQL Server database provider for SmartData.Server.

## Features

- Implements all four provider interfaces: `IDatabaseProvider`, `ISchemaProvider`, `ISchemaOperations`, `IRawDataProvider`

## Usage

```csharp
builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlServer();
```
