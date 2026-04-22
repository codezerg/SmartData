# SmartData.Console

Embedded MVC-based admin console for database inspection and management.

## Features

- Database table browser and query builder
- Backup management UI
- HTMX-powered interactive interface
- Routes under `/console/`

## Usage

```csharp
builder.Services.AddSmartDataConsole();

var app = builder.Build();
app.UseSmartDataConsole();
```
