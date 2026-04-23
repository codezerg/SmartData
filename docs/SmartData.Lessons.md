# SmartData — Lessons & Load-Bearing Decisions

Non-obvious constraints and decisions captured from debugging incidents. Read this before refactoring any of the listed subsystems — the "obvious" simplification usually regresses one of these.

## 1. SQLite `AlterColumn` must preserve the original CREATE TABLE SQL

**File:** `src/SmartData.Server.Sqlite/SqliteSchemaOperations.cs` → `AlterColumn` + helpers (`RewriteCreateTable`, `SplitTopLevel`, `PatchColumnDefinition`, `TokenizeColumnDef`, `ReadSchemaSql`, `ReadObjectSqls`, `CheckForeignKeyViolations`).

**Rule:** Read the original `sql` from `sqlite_master`, surgically patch only the target column's type / NULL-specifier, replay indexes and triggers after rename, wrap in a transaction with `PRAGMA foreign_keys=OFF`.

**Do NOT** reconstruct the CREATE TABLE from `ProviderColumnInfo`. That loses:

- `PRIMARY KEY AUTOINCREMENT` inline syntax
- Foreign keys
- `CHECK`, `DEFAULT`, `COLLATE`, `GENERATED` clauses
- `WITHOUT ROWID` / `STRICT` table tails

**Why:** SQLite has no native `ALTER COLUMN` — the 12-step table rebuild is the official remediation (https://sqlite.org/lang_altertable.html#otheralter). `ProviderColumnInfo` only carries name/type/nullable/pk/identity; anything else is silently dropped by a naive rebuild.

**Incident:** Relaxing one orphan `NOT NULL` column on `_sys_schedules` via a descriptor-based rebuild silently stripped `AUTOINCREMENT` from `Id`, which then broke every `InsertAsync` with `NOT NULL constraint failed: _sys_schedules.Id`. The scheduler couldn't reconcile and the DB had to be wiped — `ColumnsMatch` only compares type + nullable, not identity, so `SchemaManager` wouldn't notice and self-heal.

Known unexercised edges in the current tokenizer: cross-schema indexes on attached DBs (not used by SmartData's single-DB-per-context model) and `STRICT` / `WITHOUT ROWID` tails (passed through the `tail` variable verbatim but only cursorily tested).

## 2. Orphan NOT NULL columns auto-relax to nullable

**Files:** `src/SmartData.Server/SmartDataOptions.cs` (`RelaxOrphanNotNull`, default `true`), `src/SmartData.Server/SchemaManager.cs` (`RelaxOrphanColumns`), `src/SmartData.Server/SchemaLog.cs` (static log hook wired from `UseSmartData`).

**Rule:** In `SchemaMode.Auto`, if a column exists in the DB but not on the entity class, the reverse pass relaxes it from `NOT NULL` to nullable. Auto mode **never drops columns or migrates data** (see `CLAUDE.md`) — drops are intentional-only via `sp_column_drop`.

**Why:** Renaming a property creates a new column and leaves the old one. If the orphan was `NOT NULL`, every future `InsertAsync` fails. Relaxing is non-destructive and fixes inserts; drift is visible in the schema. Auto-drop was explicitly rejected — it contradicts the guarantee that makes Auto mode safe for dev and Manual mode necessary for prod.

A warning is emitted via `SchemaLog.Logger` on every relax so drift is observable in logs.

This subsystem is tied to #1: the fix for one exposed the bug in the other (same incident).

## 3. `[Tracked]` fluent mapping must be registered at startup, not on first write

**Files:** `src/SmartData.Server/Tracking/TrackingMappingRegistry.cs` (`RegisterAll`), `src/SmartData.Server/WebApplicationExtensions.cs` (calls `RegisterAll` after session hydration), `src/SmartData.Server/Tracking/TrackingWritePath.cs` (keep `tableName:` override on `InsertWithIdentity`).

**Rule:** `UseSmartData` walks every `ProcedureAssemblyRegistration` and calls `TrackingMappingRegistry.RegisterAll(types)`. All `[Tracked]` / `[Ledger]` fluent mappings are published to the shared `MappingSchema` before any HTTP request can hit `TrackingWritePath`.

**Do NOT** revert to per-first-write `RegisterHistory<T>()` + `conn.AddMappingSchema(registry.Schema)`, even with a `Lazy<bool>` barrier on the registry. It still loses under concurrency.

**Why:** LinqToDB caches **compiled insert/select expressions statically**. If the first compiled expression for `HistoryEntity<T>` is built against a partial (or missing) fluent mapping, every subsequent invocation across every connection reuses that bad descriptor. Symptoms observed under SmartApp.StressTest with 50 concurrent clients on a `[Tracked]` Customer:

| Configuration | Symptom |
|---|---|
| No `tableName:` override | `SQLite Error 1: 'no such table: HistoryEntity\`1'` on 199/200 writes |
| With `tableName:` override | `UNIQUE constraint failed: Customer_History.HistoryId` on 199/200 writes (HistoryId sent as literal 0 because identity info never made it into the cached descriptor) |
| Eager `RegisterAll` at startup | 200/200 OK |

Keep the `tableName:` override on `conn.InsertWithIdentity(history, tableName: historyTable)` as belt-and-suspenders even with eager registration — it's free insurance against any future LinqToDB descriptor-cache edge case.

The in-repo test at `tests/SmartData.TrackingTest/Program.cs` (with a 32-way concurrent-insert case) is necessary but not sufficient — this bug reproduces only under real HTTP concurrency with multiple tracked entities. Validate tracking changes against SmartApp.StressTest too.

## 4. SQLite throughput pragmas at connection open

**File:** `src/SmartData.Server.Sqlite/SqliteDatabaseProvider.cs` → `OpenConnection`.

```csharp
db.Execute("PRAGMA journal_mode = WAL;");
db.Execute("PRAGMA synchronous = NORMAL;");
db.Execute("PRAGMA busy_timeout = 5000;");
db.Execute("PRAGMA cache_size = -20000;");
db.Execute("PRAGMA temp_store = MEMORY;");
```

**Measured impact** (SmartApp.StressTest, 50 concurrent clients, tracked Customer with `FullTextIndex`):

| | Before (WAL only) | After (all pragmas) | Delta |
|---|---|---|---|
| Write throughput | 124 req/s | 362 req/s | +193% |
| Write p50 | 82ms | 37ms | −54% |
| Write p99 | 1459ms | 443ms | −70% |
| Writes OK | 199/200 | 200/200 | +1 stable (combined with #3) |

Per-pragma rationale:

- `synchronous = NORMAL` — under WAL, one `fsync` per checkpoint instead of per commit. Safe; only risk is losing the last few commits on a power cut, never corruption. `FULL` is paranoid for WAL.
- `busy_timeout = 5000` — SQLite's own busy handler waits up to 5s for a writer lock. Without it, concurrent writers get `SQLITE_BUSY` and the ADO.NET layer retries unpredictably.
- `cache_size = -20000` — ~20 MB page cache (negative = KB). Hot indexes and recent `_History` rows stay resident across transactions.
- `temp_store = MEMORY` — sort spills and temp B-trees stay in RAM.

**A/B note:** Disabling `SmartDataOptions.Metrics.Enabled` did NOT help — tail latency got *worse* in our measurements (likely fewer yield points → longer lock queues). The `SqlTrackingInterceptor` is not a bottleneck. Leave metrics on.

**Structural ceiling:** SQLite's single-writer lock caps single-file throughput at ~200–400 writes/s on typical hardware. Options on the shelf if this ceiling matters (none implemented):

1. Serialize tracked writes through a single `Channel<T>` drained by one task (removes lock-contention jitter).
2. Move FTS to async background maintenance (shortens the writer-locked transaction).
3. Switch to SQL Server for real write load.

## 5. Dev loop: ProjectReference, not PackageReference

**Files:** `Directory.Build.props` (CalVer stamping), `scripts/pack.ps1` / `scripts/pack.sh` (frozen timestamp per run).

**Rule:** For active co-development (e.g. SmartData + SmartApp), use `ProjectReference` from the consumer. Only use `PackageReference` when testing release packaging end-to-end.

**Why:** NuGet's package cache at `~/.nuget/packages/smartdata.server/<version>/` keeps the first `.nupkg` extracted for a given version string. Re-running `dotnet pack` that produces the same version does NOT invalidate the cache — `dotnet restore` sees "X.Y.Z already present" and skips. A whole debugging cycle was wasted once chasing a "fix isn't taking" phantom that was actually stale DLLs in the cache.

For release packaging when ProjectReference isn't an option:

- `Directory.Build.props` stamps 4-part CalVer `yy.M.d.HHmmss` (e.g. `26.4.23.90442`). 4-part because NuGet accepts it and it sidesteps semver's "no leading zero on numeric prerelease identifier" rule — no `-t…` suffix needed. Assembly + File versions pinned to `1.0.0.0` because .NET assembly components are `uint16`-capped and `HHmmss` can exceed 65535.
- `scripts/pack.ps1` (Windows) / `scripts/pack.sh` (bash) freeze the stamp via `SMARTDATA_STAMP` env var so every project in one pack run gets the same version (otherwise each project re-evaluates `DateTime.UtcNow` in its own MSBuild context and transitive `ProjectReference` packages get mismatched versions and fail to restore).
- Consumer floats via `Version="*"` with a local file feed (`NuGet.config` adds `C:\GitHub\SmartData\artifacts`).
- Artifacts land in `./artifacts/` (set by `Directory.Build.props`).

**Cache purge** as a last resort if you suspect stale extraction:

```
rm -rf ~/.nuget/packages/smartdata.* && dotnet restore --force
```

## 7. SQLitePCLRaw bundles are process-exclusive

**Files:** `src/SmartData.Server.Sqlite/*.csproj` (transitively `bundle_e_sqlite3` via `Microsoft.Data.Sqlite`) vs. `src/SmartData.Server.SqliteEncrypted/*.csproj` (`bundle_e_sqlcipher`).

**Rule:** A process can load **one** `SQLitePCLRaw` bundle, not both. The encrypted provider ships as a separate NuGet package precisely so transitive graphs cannot silently end up with both — consumers must choose one or the other at the app level.

**Why:** Both bundles register native `sqlite3_*` symbols via `SQLitePCL.Batteries_V2.Init()`. Whichever one runs first wins; the other's initialization is a no-op but any code that relied on the losing bundle's behavior quietly diverges. Detected by crashes on first `Open()` when both flow in transitively.

**Do NOT** add a `SmartData.Server.Sqlite` flag that swaps bundles at runtime — it would make the conflict internal and invisible. Two packages, loud README, clean break.

## 8. SQLCipher `PRAGMA key` must run before every other statement

**Files:** `src/SmartData.Server.Sqlite/SqliteDatabaseProvider.cs` (`OnConnectionOpened` hook between construction and `ApplyPragmas`), `src/SmartData.Server.SqliteEncrypted/Encrypted*Provider.cs` (each overrides `OpenConnection` to run `ApplyKey` immediately after `conn.Open()`).

**Rule:** Every freshly-opened connection — `DataConnection` or raw `SqliteConnection` — must issue `PRAGMA key = …` as its first statement. Only then can pragmas, reads, or writes run.

**Why:** SQLCipher intercepts the first real read and errors if the key was never supplied, but `PRAGMA key` itself always returns success. Meaning: if you issue `PRAGMA journal_mode` first and then `PRAGMA key`, the journal pragma silently fails under the hood and later reads error with `file is not a database`. The `OnConnectionOpened` hook exists as a separate step *before* `ApplyPragmas` for exactly this ordering, and each sub-provider in `SmartData.Server.Sqlite` had to grow its own override seam because they each open raw `SqliteConnection` instances outside the `DataConnection` path.

**Verification for rekey:** `PRAGMA key` never errors, so the rekey path forces a `SELECT count(*) FROM sqlite_master` read immediately after keying to validate the current key before calling `PRAGMA rekey`. Without that forced read, a wrong `CurrentKey` would silently proceed to rekey with no data visible — catastrophic.

## 9. `SchemaManager<T>._ensured` is process-static and directory-unaware

**File:** `src/SmartData.Server/SchemaManager.cs` — `private static readonly HashSet<string> _ensured` keyed by `$"{dbName}::{tableName}"`.

**Rule:** `SchemaManager<T>.EnsureSchema(dbName, provider)` remembers it has ensured `(dbName, table)` for the rest of the process. The key does **not** include the data directory or the provider instance.

**Why it bites test harnesses:** the obvious "fresh temp dir per test" isolation pattern silently fails. Test N creates master.db in `/tmp/a/`, populates `_ensured` with `"master::_sys_users"`. Test N+1 points a fresh provider at `/tmp/b/master.db` (empty file) — `EnsureMasterDatabase` → `SchemaManager<SysUser>.EnsureSchema` short-circuits on the cached key, skips `CreateTable`, and every subsequent query errors with `no such table: _sys_users`. The error surfaces far from the cause.

**Workarounds:**
- Share one app + one data dir across tests (what `tests/SmartData.Server.SqliteEncrypted.Tests/` does); order mutating tests last.
- Or reflection-clear the static field between tests (fragile — only acceptable if per-test process isolation is too expensive).
- Do **not** try to solve it by making `_ensured` instance-scoped on a manager: every consumer today assumes once-per-process ensure semantics, and loosening that would re-check schema on every request.

Detected when the encrypted provider's test harness failed five tests with `no such table: _sys_users` after the per-test temp-dir rework.

## Related docs

- `CLAUDE.md` — conventions, build commands, architecture overview (the "onboarding" doc)
- `docs/SmartData.Guide.md` — developer guide for writing entities and procedures
- `docs/SmartData.Server.Tracking.md` — tracking / ledger spec
- Long-form design pool: `docs/SmartData.*.md`
