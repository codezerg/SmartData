# SmartData.Cli

Command-line tool for managing SmartData servers, databases, tables, and data. Published as `sd.exe` in the repo's `bin/` directory.

- **Target:** .NET 10 (self-contained, single-file)
- **Assembly name:** `sd`
- **Dependency:** SmartData.Client → SmartData.Core
- **Protocol:** Binary RPC over HTTP POST to `/rpc`
- **Config:** `~/.sd/config.json` (persists server, token, database)

## Configuration

Settings are stored at `~/.sd/config.json` and loaded automatically on startup.

| Field | Description |
|-------|-------------|
| `Server` | Active server URL |
| `Token` | Auth token (set by `login`) |
| `Database` | Active database (set by `db use`) |

## Commands

### Server & Auth

| Command | Description |
|---------|-------------|
| `sd connect <server>` | Set active server (auto-prepends `http://` if needed) |
| `sd login [--username x] [--password y]` | Authenticate and store token |
| `sd logout` | Clear stored token |
| `sd status` | Get server status via `sp_status` |
| `sd user create --username x --password y` | Create a new user account |

### Database

| Command | Description |
|---------|-------------|
| `sd db list` | List all databases |
| `sd db create <name>` | Create a new database |
| `sd db drop <name>` | Delete a database |
| `sd db use <name>` | Set active database (saved in config) |

### Tables

| Command | Description |
|---------|-------------|
| `sd table list` | List tables in active database |
| `sd table create <name> --col Name:Type[:pk] ...` | Create table with columns |
| `sd table drop <name>` | Delete a table |
| `sd table rename <name> <new-name>` | Rename a table |
| `sd table describe <name>` | Show columns and indexes |

Column type syntax: `Name:Type` for required, `Name:Type?` for nullable, `Name:Type:pk` for primary key.

```bash
sd table create users --col Id:int:pk --col Email:string --col Bio:string?
```

### Columns

| Command | Description |
|---------|-------------|
| `sd column add <table> <name> <type> [--nullable]` | Add a column |
| `sd column drop <table> <name>` | Remove a column |
| `sd column rename <table> <name> <new-name>` | Rename a column |

### Indexes

| Command | Description |
|---------|-------------|
| `sd index list <table>` | List indexes on a table |
| `sd index create <table> <name> --columns Col1,Col2 [--unique]` | Create an index |
| `sd index drop <table> <name>` | Drop an index |

### Data (CRUD)

| Command | Description |
|---------|-------------|
| `sd select <table> [--where '{}'] [--orderby Col[:desc]] [--limit N] [--offset N]` | Query rows |
| `sd insert <table> --Col1 val1 --Col2 val2` | Insert a row |
| `sd update <table> --where '{}' --set '{}'` | Update rows |
| `sd delete <table> --where '{}'` | Delete rows |

Where clauses use JSON filter syntax:

```bash
sd select users --where '{"Age":{"$gt":18}}' --orderby Name --limit 10
```

### Import / Export

| Command | Description |
|---------|-------------|
| `sd export <table> [--where '{}'] [--out file.json]` | Export table data to JSON |
| `sd import <table> [--file data.json]` | Import data from JSON |

### Stored Procedures

| Command | Description |
|---------|-------------|
| `sd exec <sp-name> [--param1 val1 ...]` | Execute a stored procedure |
| `sd sp errors [name] [--limit N]` | View procedure compilation/runtime errors |

### Backup

Supports resumable chunked transfers (1 MB chunks).

| Command | Description |
|---------|-------------|
| `sd backup create <db1,db2 or *> [--no-wait]` | Create backup (async job — polls progress by default, `--no-wait` returns job ID immediately) |
| `sd backup restore <backup-id> [--force] [--no-wait]` | Restore from backup (async job — same polling/no-wait behavior) |
| `sd backup status <job-id>` | Check status of a running backup/restore job |
| `sd backup cancel <job-id>` | Cancel a running backup/restore job |
| `sd backup list` | List available backups |
| `sd backup drop <backup-id>` | Delete a backup |
| `sd backup download <id> [--out file.smartbackup]` | Download backup |
| `sd backup upload --file backup.smartbackup` | Upload backup |
| `sd backup history` | View backup operation history |
| `sd backup verify <file.smartbackup>` | Verify backup file integrity (local, no server call) |

### Settings

| Command | Description |
|---------|-------------|
| `sd settings list` | List all settings (sections, values, read-only status) |
| `sd settings get <key>` | Get a single setting value by key |
| `sd settings set <key> <value>` | Update a runtime-tunable setting |

Settings are persisted in the `_sys_settings` table. Startup-only settings (SchemaMode, Index.*) are read-only and cannot be changed via `set`.

### Other

| Command | Description |
|---------|-------------|
| `sd storage [--db db]` | View storage usage |
| `sd logs [--limit N]` | View server logs (default 50) |
| `sd dump [--out ./dump.md]` | Dump full database schema to markdown |

### Metrics & Observability

| Command | Description |
|---------|-------------|
| `sd metrics list [--name pattern] [--type counter\|histogram\|gauge] [--source live\|db]` | View metrics |
| `sd metrics watch [--interval 5] [--name pattern]` | Live-refresh metrics display |
| `sd metrics traces [--procedure X] [--errors] [--min-duration 500] [--source live\|db]` | List traces |
| `sd metrics trace <traceId>` | Show full span tree for a trace |
| `sd metrics exceptions [--type X] [--procedure Y] [--source live\|db]` | List exceptions |

## Project Structure

```
SmartData.Cli/
├── Program.cs             Entry point, command routing
├── SdConfig.cs            Config persistence (~/.sd/config.json)
├── ApiClient.cs           HTTP client wrapper for /rpc
├── ArgParser.cs           CLI argument parsing (GetFlag, HasFlag, GetAllFlags)
└── Commands/
    ├── ConnectCommand.cs
    ├── LoginCommand.cs
    ├── UserCommand.cs
    ├── DbCommand.cs
    ├── TableCommand.cs
    ├── ColumnCommand.cs
    ├── IndexCommand.cs
    ├── DataCommand.cs       Select, Insert, Update, Delete
    ├── ExportImportCommand.cs
    ├── ExecCommand.cs
    ├── DumpCommand.cs
    ├── BackupCommand.cs
    ├── StorageCommand.cs
    ├── SpCommand.cs
    ├── SettingsCommand.cs   Settings list, get, set
    ├── LogsCommand.cs
    └── MetricsCommand.cs    Metrics, traces, exceptions
```

## Communication

`ApiClient` wraps `SmartDataClient` and sends `CommandRequest` objects (command name + token + database + binary-serialized args) to the server's `/rpc` endpoint. Responses arrive as `CommandResponse` and are printed as pretty-printed JSON to stdout.
