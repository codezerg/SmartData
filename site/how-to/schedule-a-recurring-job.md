---
title: Schedule a recurring job
description: Turn a procedure into a scheduled job with a single attribute.
---

Decorate a procedure with a cadence attribute. Call `AddSmartDataScheduler()` **after** `AddStoredProcedures`. That's the entire setup.

```csharp
using SmartData.Core;
using SmartData.Server.Procedures;
using SmartData.Server.Scheduling.Attributes;

[Job("Nightly cleanup", Category = "Ops")]
[Daily("03:15")]
[Retry(attempts: 3, intervalSeconds: 60)]
public class NightlyCleanup : AsyncStoredProcedure<VoidResult>
{
    public override async Task<VoidResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
    {
        // ... your work ...
        return VoidResult.Instance;
    }
}
```

```csharp
// Program.cs — order matters
builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
builder.Services.AddSmartDataScheduler();   // AFTER AddStoredProcedures
```

On next startup, the reconciler writes a row into `_sys_schedules`. The scheduler tick claims it when due and fires the procedure.

## Picking a cadence

```csharp
[Daily("02:00")]                               // once a day
[Daily("02:00", Days = Days.Weekdays)]         // weekdays only
[Every(5, Unit.Minutes)]                       // every 5 min on wall clock
[Every(5, Unit.Minutes, Between = "09:00-17:00")]  // business-hours poll
[Weekly(Days.Mon | Days.Fri, "06:00")]         // Mon + Fri at 06:00
[Monthly(Day.D1 | Day.Last, "00:30")]          // 1st and last of month
[MonthlyDow(Weeks.First, Days.Mon, "06:00")]   // first Monday of month
[Once("2026-06-01 09:00")]                     // one-shot — auto-disables
```

Stack attributes to fire multiple times:

```csharp
[Daily("09:00")]
[Daily("17:00")]
public class TwiceDaily : AsyncStoredProcedure<VoidResult> { /* ... */ }
```

Each attribute produces a distinct schedule row (`Daily_09_00`, `Daily_17_00`).

## Retry — read carefully

```csharp
[Retry(attempts: 3, intervalSeconds: 60)]
```

`attempts: 3` means **3 total runs** (1 initial + 2 retries) — not "3 retries after the first." `attempts: 1` is equivalent to no retry. `ErrorSeverity.Fatal` short-circuits retry.

## What users can tweak at runtime

Users (through `sp_schedule_update` or the admin console) can change:
- `Enabled` on/off
- `RetryAttempts`, `RetryIntervalSeconds`, `JitterSeconds`

They **cannot** change when a job fires — that's code. The reconciler overwrites timing fields from attributes on every startup.

## Run now / cancel / history

- **Run now:** `sp_schedule_start` — claims a run immediately.
- **Cancel:** `sp_schedule_cancel` — cooperative, in-flight job observes via `CancellationToken`.
- **History:** `sp_schedule_history` — run records with outcome, duration, attempt number.

## Reading settings inside a job

The scheduler passes no arguments. If a job needs config:

```csharp
public override async Task<VoidResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
{
    var cutoffDays = int.Parse(
        ctx.GetTable<Setting>().First(s => s.Key == "Cleanup.CutoffDays").Value);
    // ...
}
```

## Related

- [Scheduling](/fundamentals/scheduling/) — full mental model, retry semantics, catch-up
- [Procedures](/fundamentals/procedures/) — base classes
- [System procedures → Scheduling](/reference/system-procedures/) — `sp_schedule_*`
