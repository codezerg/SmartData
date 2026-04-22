---
title: Scheduling
description: Attribute-driven recurring jobs. A procedure + one attribute is a job.
---

SmartData ships a first-class scheduler. The design is deliberately opinionated:

> **A scheduled job is a stored procedure with a `[Daily]` on it. Developers change what it does; users change when it runs.**

No separate project, no job-definition DSL, no workflow engine inside the database. Decorate a procedure, call `AddSmartDataScheduler()`, done.

## Minimal example

```csharp
using SmartData.Core;
using SmartData.Server.Procedures;
using SmartData.Server.Scheduling.Attributes;

[Job("Nightly DB Maintenance", Category = "Ops",
     Description = "Vacuums stale rows and reindexes hot tables.")]
[Daily("03:15")]
[Retry(attempts: 3, intervalSeconds: 60)]
public class NightlyCleanup : AsyncStoredProcedure<VoidResult>
{
    public override async Task<VoidResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
    {
        // ... work ...
        return VoidResult.Instance;
    }
}
```

Wire the scheduler in `Program.cs` **after** `AddStoredProcedures` so the reconciler sees your assembly:

```csharp
builder.Services.AddSmartData();
builder.Services.AddSmartDataSqlite();
builder.Services.AddStoredProcedures(typeof(Program).Assembly);
builder.Services.AddSmartDataScheduler();
```

At startup the reconciler writes a `_sys_schedules` row for `NightlyCleanup`. From then on, a hosted service polls `sp_scheduler_tick` every 15 seconds (default), claims due schedules, and executes them.

## Schedule attributes

Times are **server local time**. Calendar filters (`Days`, `Months`, `Weeks`, `Between`) are `[Flags]` enums that compose onto any cadence.

| Attribute | Example | Meaning |
| --- | --- | --- |
| `[Daily]` | `[Daily("02:00")]` | Once per day at `HH:mm`. Add `Days = Days.Weekdays` to narrow. |
| `[Every]` | `[Every(5, Unit.Minutes)]` | Wall-clock anchors — fires at `:00`, `:05`, `:10`. `Between = "09:00-17:00"` bounds a window. |
| `[Weekly]` | `[Weekly(Days.Mon \| Days.Fri, "06:00")]` | Selected weekdays at a time. `Every = N` for biweekly etc. |
| `[Monthly]` | `[Monthly(Day.D1 \| Day.Last, "00:30")]` | Specific calendar days. `Day.Last` = end-of-month; missing days silently skip. |
| `[MonthlyDow]` | `[MonthlyDow(Weeks.First, Days.Mon, "06:00")]` | Nth weekday of month — "first Monday", "last Friday". |
| `[Once]` | `[Once("2026-06-01 09:00")]` | One-shot. Schedule auto-disables after it fires. |
| `[Job]` | `[Job("Name", Category = "Ops", Description = "...")]` | Display metadata — code-only, never persisted. |
| `[Retry]` | `[Retry(attempts: 3, intervalSeconds: 60)]` | See below. |

Stack multiple cadence attributes to fire multiple times: `[Daily("09:00")] [Daily("17:00")]` produces two rows, `Daily_09_00` and `Daily_17_00`. Reordering doesn't change the names, so user customizations stay attached.

## Retry semantics — read carefully

> ⚠️ `[Retry(attempts: 3)]` means **3 total runs** (1 initial + 2 retries), not "3 retries after the first." `attempts: 1` is equivalent to no retry.

Retry is a row edit, not a queue. On failure, `sp_schedule_execute` stamps `NextAttemptAt` on the failed run; the next tick picks it up and fires a fresh run with `AttemptNumber + 1`. `ErrorSeverity.Fatal` short-circuits retry — no further attempts.

## Developer/user split

The split is load-bearing. Users cannot change *when* a job fires from the admin console; that's a code change and restart.

| Area | Owner | Mechanism |
| --- | --- | --- |
| Which procedures are schedulable | Developer | `[Daily]`/`[Every]`/… attribute on the class |
| What a procedure does | Developer | `ExecuteAsync` body |
| **When** a schedule fires | Developer | Attribute arguments — overwritten into the DB every startup |
| Whether it's enabled | User | `sp_schedule_update` / console toggle |
| Retry attempts / interval / jitter | User | `sp_schedule_update` — preserved across reconciles |

The reconciler always overwrites timing fields from the attribute and preserves only four user-controlled fields: `Enabled`, `RetryAttempts`, `RetryIntervalSeconds`, `JitterSeconds`. Removing an attribute disables the row but retains history.

## Manual trigger, cancel, history

- **Run now** — `sp_schedule_start`. Claims a run immediately, outside the schedule timeline.
- **Cancel** — `sp_schedule_cancel`. Cooperative: the in-flight procedure sees cancellation via its `CancellationToken` within a few seconds.
- **History** — `sp_schedule_history`. Run records with start/finish, duration, outcome, message, attempt number, originating instance id. Filter by outcome/procedure/date.

## Multi-instance safety

Running multiple SmartData servers against the same database is explicitly supported. A unique-index claim means only one instance can pick up a given fire. Heartbeats prevent long-running work on one node from being mistaken for a crashed claim on another. Cancels propagate across nodes via the shared `_sys_schedule_runs` row.

## Catch-up policy

If the scheduler was down when a fire was due, the default is to **drop** the missed fire and roll `NextRunOn` forward. Set `SchedulerOptions.MaxCatchUp` to a small integer to queue up to N missed fires after recovery — **only enable for idempotent jobs**. Replaying hours of accumulated fires is almost always wrong (duplicated reports, re-sent emails).

## Configuration

```csharp
builder.Services.AddSmartDataScheduler(o =>
{
    o.Enabled              = true;
    o.PollInterval         = TimeSpan.FromSeconds(15);
    o.MaxConcurrentRuns    = 4;
    o.HistoryRetentionDays = 30;
    o.HeartbeatInterval    = TimeSpan.FromSeconds(3);
    o.OrphanTimeout        = TimeSpan.FromMinutes(5);
    o.MaxCatchUp           = 0;     // 0 = drop; >0 = queue up to N
});
```

Setting `Enabled = false` keeps reconciliation running (schedules stay visible in `sp_schedule_list`) but stops the tick — handy for deploying code to worker nodes that shouldn't also run the scheduler.

## What the scheduler deliberately does not do

- **No multi-step jobs.** Workflow composes in C# via `ctx.ExecuteAsync<T>()` and `await` — not in database rows. A worse programming language inside the database is not what anyone needs.
- **No runtime procedure registration.** Schedules for unknown procedures are rejected. The set of schedulable procedures is closed to what's in code.
- **No arguments to target procedures.** Scheduled calls pass nothing. If a job needs configuration, read it from a `Setting` table inside `ExecuteAsync`.
- **No per-schedule timezone.** All times are server local. Run the process in UTC if you need UTC-stable semantics.

## Related

- [Schedule a recurring job](/how-to/schedule-a-recurring-job/) — a concrete recipe
- [Procedures](/fundamentals/procedures/) — the base classes jobs inherit from
- [SmartData.Server reference — Scheduling](/reference/smartdata-server/) — entities, reconciliation rules, execution path
- [System procedures → Scheduling](/reference/system-procedures/) — `sp_schedule_*` surface
