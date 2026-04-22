---
title: Build a CRUD app
description: End-to-end walkthrough — entity, four procedures, client call.
---

Build a minimal task tracker. One entity, four procedures, one client call. About 20 minutes.

Prereqs: .NET 10 SDK installed, [install](/get-started/install/) done so package references resolve.

## 1. Create the project

```bash
mkdir TaskTracker && cd TaskTracker
dotnet new web -n TaskTracker.Server
cd TaskTracker.Server

dotnet add package SmartData.Server
dotnet add package SmartData.Server.Sqlite
```

## 2. Wire it up

Replace `Program.cs`:

```csharp
using SmartData;                     // AddSmartData, AddStoredProcedures, UseSmartData
using SmartData.Server.Sqlite;       // AddSmartDataSqlite

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);

var app = builder.Build();
app.UseSmartData();   // maps POST /rpc + GET /health
app.Run();
```

Schema mode defaults to `Auto` — the first call to each entity creates/migrates the table. Fine for this walkthrough. See [Providers](/fundamentals/providers/) for production guidance.

## 3. Define the entity

`Entities/TaskItem.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using LinqToDB.Mapping;
using SmartData.Server.Attributes;

namespace TaskTracker.Server.Entities;

[Table]
[Index("IX_Task_Status", nameof(Status))]
public class TaskItem
{
    [PrimaryKey, Identity] public int      Id        { get; set; }
    [Column] public string   Title     { get; set; } = "";
    [Column] public string   Status    { get; set; } = "open";   // open | done
    [Column] public DateTime CreatedOn { get; set; }
    [Column] public DateTime? CompletedOn { get; set; }
}
```

`[Index]` from `SmartData.Server.Attributes` is auto-created by AutoRepo. See [Entities](/fundamentals/entities/).

## 4. DTOs

One folder per procedure, shared types in `Common/`.

`Contracts/Common/SaveResult.cs`:

```csharp
namespace TaskTracker.Server.Contracts.Common;

public class SaveResult
{
    public string Message { get; set; } = "";
    public int    Id      { get; set; }
}

public class DeleteResult
{
    public string Message { get; set; } = "";
}
```

`Contracts/TaskList/TaskListResult.cs`:

```csharp
namespace TaskTracker.Server.Contracts.TaskList;

public class TaskListResult
{
    public List<TaskItemDto> Items { get; set; } = new();
    public int               Total { get; set; }
}

public class TaskItemDto
{
    public int      Id          { get; set; }
    public string   Title       { get; set; } = "";
    public string   Status      { get; set; } = "";
    public DateTime CreatedOn   { get; set; }
    public DateTime? CompletedOn { get; set; }
}
```

Why DTOs and not the entity? [Return DTOs, not entities](/how-to/return-dtos-not-entities/).

## 5. The four procedures

`Procedures/TaskList.cs`:

```csharp
using SmartData.Server.Procedures;
using TaskTracker.Server.Contracts.TaskList;
using TaskTracker.Server.Entities;

namespace TaskTracker.Server.Procedures;

public class TaskList : StoredProcedure<TaskListResult>
{
    public string? Status { get; set; }

    public TaskList(IDatabaseContext ctx) { }

    public override TaskListResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var query = ctx.GetTable<TaskItem>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(Status))
            query = query.Where(t => t.Status == Status);

        var items = query
            .OrderByDescending(t => t.CreatedOn)
            .Select(t => new TaskItemDto
            {
                Id = t.Id, Title = t.Title, Status = t.Status,
                CreatedOn = t.CreatedOn, CompletedOn = t.CompletedOn
            })
            .ToList();

        return new TaskListResult { Items = items, Total = items.Count };
    }
}
```

`Procedures/TaskSave.cs` — insert when `Id == 0`, update otherwise:

```csharp
using SmartData.Server.Procedures;
using TaskTracker.Server.Contracts.Common;
using TaskTracker.Server.Entities;

namespace TaskTracker.Server.Procedures;

public class TaskSave : StoredProcedure<SaveResult>
{
    public int    Id     { get; set; }
    public string Title  { get; set; } = "";
    public string Status { get; set; } = "open";

    public TaskSave(IDatabaseContext ctx) { }

    public override SaveResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(Title))
            RaiseError(1001, "Title is required.");

        if (Id == 0)
        {
            var inserted = ctx.Insert(new TaskItem
            {
                Title     = Title,
                Status    = Status,
                CreatedOn = DateTime.UtcNow
            });
            return new SaveResult { Id = inserted.Id, Message = "Created." };
        }

        var existing = ctx.GetTable<TaskItem>().FirstOrDefault(t => t.Id == Id)
            ?? throw RaiseAndReturn();
        existing.Title  = Title;
        existing.Status = Status;
        if (Status == "done" && existing.CompletedOn == null)
            existing.CompletedOn = DateTime.UtcNow;
        ctx.Update(existing);
        return new SaveResult { Id = existing.Id, Message = "Updated." };

        ProcedureException RaiseAndReturn() { RaiseError(1002, "Task not found."); return null!; }
    }
}
```

`RaiseError` is `[DoesNotReturn]` — nullable flow-analysis works. See [Procedures → Errors](/fundamentals/procedures/).

`Procedures/TaskDelete.cs`:

```csharp
using SmartData.Server.Procedures;
using TaskTracker.Server.Contracts.Common;
using TaskTracker.Server.Entities;

namespace TaskTracker.Server.Procedures;

public class TaskDelete : StoredProcedure<DeleteResult>
{
    public int Id { get; set; }

    public TaskDelete(IDatabaseContext ctx) { }

    public override DeleteResult Execute(IDatabaseContext ctx, CancellationToken ct)
    {
        var affected = ctx.Delete<TaskItem>(t => t.Id == Id);
        if (affected == 0) RaiseError(1003, "Task not found.");
        return new DeleteResult { Message = "Deleted." };
    }
}
```

## 6. Run the server

```bash
dotnet run
```

First request creates `data/master.db` and the `TaskItem` table. `GET http://localhost:5000/health` should return `Healthy`.

## 7. Call it from a client

New console project in a sibling folder:

```bash
cd ..
dotnet new console -n TaskTracker.Demo
cd TaskTracker.Demo
dotnet add package SmartData.Client
```

`Program.cs`:

```csharp
using SmartData.Client;

var client = new SmartDataClient("http://localhost:5000");

// 1. Log in — the framework ships with admin:admin on the master DB.
var login = await client.SendAsync("sp_login", new()
{
    ["Username"] = "admin",
    ["Password"] = "admin"
});
client.Token    = login.GetData<Dictionary<string, object?>>()!["Token"]!.ToString();
client.Database = "master";

// 2. Create a task
var save = await client.SendAsync("usp_task_save", new()
{
    ["Title"] = "Ship the tutorial"
});
Console.WriteLine(save.GetDataAsJson());

// 3. List tasks
var list = await client.SendAsync("usp_task_list");
Console.WriteLine(list.GetDataAsJson());
```

Run both projects and you should see the task round-trip.

Details on the wire format and the `Token` / `Database` flow: [Binary RPC](/fundamentals/binary-rpc/) and [Call procedures from a client](/how-to/call-procedures-from-a-client/).

## Where to go next

- **Auth and permissions.** The admin user bypasses all permission checks. Before exposing anything, create real users and grant scoped permissions — see the [Users routes in the Console](/reference/smartdata-console/#users) or call `sp_user_create` + `sp_user_permission_grant` directly.
- **Admin UI.** Add `SmartData.Console` to explore tables, run procedures, and watch metrics live — [how-to](/how-to/use-the-admin-console/).
- **Background jobs.** Turn a procedure into a nightly job with `[Daily]`/`[Every]` — [Schedule a recurring job](/how-to/schedule-a-recurring-job/).
- **Change tracking.** Add `[Tracked]` to the entity for row-level audit history — [Enable change tracking](/how-to/enable-change-tracking/).
