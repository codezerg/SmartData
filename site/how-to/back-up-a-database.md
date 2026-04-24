---
title: Back up a database
description: Trigger, list, download, and restore database backups.
---

Backups go through system procedures. No CLI flags or external tools required.

## Create a backup

```csharp
var result = await procedures.ExecuteAsync<BackupCreateResult>(
    "sp_backup_create",
    new
    {
        Database = "master",
        Note     = "Pre-deploy snapshot",
    });
// result.BackupId, result.FilePath
```

The call returns immediately; the backup runs in the background. Poll with `sp_backup_status`.

## Via the `sd` CLI

```bash
sd connect https://your-server
sd login
sd backup create --db master --note "Pre-deploy snapshot"
sd backup status <backup-id>
sd backup list --db master
```

## Listing, downloading, restoring

```csharp
// List
var backups = await procedures.ExecuteAsync<BackupListResult>(
    "sp_backup_list", new { Database = "master" });

// Download (returns a byte stream as base64 / binary payload)
var bytes = await procedures.ExecuteAsync<BackupDownloadResult>(
    "sp_backup_download", new { BackupId = id });

// Restore
await procedures.ExecuteAsync<VoidResult>(
    "sp_backup_restore",
    new
    {
        Database = "master",
        BackupId = id,
    });
```

`sp_backup_upload` accepts a byte payload for restoring from an external file.

## In-flight control

- **Cancel:** `sp_backup_cancel` — cooperative stop; in-flight file is cleaned up.
- **History:** `sp_backup_history` — records of past backups + outcomes.
- **Drop:** `sp_backup_drop` — delete a backup file + its history row.

## Scheduling a nightly backup

Combine backup procedures with the scheduler:

```csharp
[Job("Nightly backup", Category = "Ops")]
[Daily("02:30")]
public class NightlyBackup : AsyncStoredProcedure<VoidResult>
{
    public override async Task<VoidResult> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct)
    {
        await ctx.ExecuteAsync<BackupCreateResult>(
            "sp_backup_create",
            new { Database = ctx.DatabaseName, Note = $"Scheduled {DateTime.UtcNow:u}" });

        return VoidResult.Instance;
    }
}
```

## What the provider does under the hood

- **SQLite** — `VACUUM INTO` against a staging file, then moved into the backups directory.
- **SQL Server** — `BACKUP DATABASE` to a provider-managed location, verified after write.

Each provider translates the system procedure calls into its native mechanism. From your code, the interface is identical.

## Related

- [Providers](/fundamentals/providers/) — how providers implement the backup interface
- [Schedule a recurring job](/how-to/schedule-a-recurring-job/) — nightly backup pattern
- [System procedures → Backup](/reference/system-procedures/) — `sp_backup_*` surface
- [SmartData.Cli reference](/reference/smartdata-cli/) — `sd backup *` commands
