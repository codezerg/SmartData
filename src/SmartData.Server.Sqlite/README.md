# SmartData.Server.Sqlite

SQLite database provider for SmartData.Server.

## Features

- Implements all four provider interfaces: `IDatabaseProvider`, `ISchemaProvider`, `ISchemaOperations`, `IRawDataProvider`
- File-based databases stored in `data/{dbName}.db`

## Usage

```csharp
builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
```
