---
title: Tracking & Ledger
description: Opt-in entity mutation history features.
---

Two opt-in features for capturing entity mutation history, applied at the class level:

- **`[Tracked]`** — queryable history. Every `INSERT` / `UPDATE` / `DELETE` routed through `IDatabaseContext` writes a mirrored row to `{Table}_History` with audit metadata (operation, timestamp, user).
- **`[Ledger]`** — queryable history *plus* integrity hashing. Writes to both `{Table}_History` and `{Table}_Ledger`. The ledger table chains SHA-256 hashes so tampering with past rows is detectable — given external anchoring (see § Digests & Anchoring).

`[Ledger]` implies `[Tracked]`. The two tables are independent on the wire: query `_History` for ergonomics (joins, projections, LINQ), query and verify `_Ledger` for integrity. There is no FK between them.

## Quick start

```csharp
using SmartData.Server.Attributes;

[Table]
[Ledger]                        // implies [Tracked]; drop to [Tracked] for history-only
public class Invoice
{
    [PrimaryKey, Identity, Column] public int Id { get; set; }
    [Column] public string Customer { get; set; } = "";
    [Column] public decimal Amount { get; set; }
    [Column, NotTracked] public string? InternalNotes { get; set; }   // excluded from history + ledger
}
```

Writes go through the usual entity APIs — no new call site to learn. `ctx.Insert(invoice)` produces one `{Invoice_History}` row and, for ledgered entities, one paired `{Invoice_Ledger}` row hashed onto the chain.

Read back:

```csharp
var recent = await ctx.History<Invoice>()
    .Where(h => h.Data.Id == 42)
    .OrderByDescending(h => h.ChangedOn)
    .Take(10)
    .ToListAsync();

var verification = ctx.Verify<Invoice>();   // VerificationResult — see § Read Path
var digest = ctx.LedgerDigest<Invoice>();   // capture for external anchoring
```

Schema tables are provisioned the first time the entity is used; no migration step is required.

## Goals

- Capture every entity mutation (I/U/D) made through AutoRepo / linq2db entity APIs.
- Make history fully queryable via normal LINQ against the same entity type.
- Support tamper-evident audit on opt-in tables via per-row cryptographic chaining.
- Survive schema evolution (add/drop/rename columns) without invalidating past data.
- Work uniformly across all database providers (SQLite, SQL Server, etc.).

## Non-goals

- Providing tamper-*proof* (vs. tamper-*evident*) integrity. See § Digests & Anchoring.
- Capturing mutations made via raw SQL or direct `IDbConnection` access.
- Bitemporal `AsOf(DateTime)` queries as a first-class API — emulate via `_History` lookup by PK + timestamp, see § Bitemporal queries.
- Replicating or synchronizing ledger data to external systems.

## Attributes

| Attribute | Target | Effect |
|-----------|--------|--------|
| `[Tracked]` | Entity class | Provisions `{Table}_History`; writes a post-image row on every I/U/D |
| `[Ledger]` | Entity class | Implies `[Tracked]`; also provisions `{Table}_Ledger`; writes chained + hashed canonical bytes on every I/U/D |
| `[NotTracked]` | Property | Excludes property from both history and ledger. Toggling mid-life is allowed and detected — see § Schema Drift. |

## Storage

### `{Table}_History`

Provisioned by AutoRepo to mirror the source entity's columns (honoring `[NotTracked]` exclusions), plus audit fields:

| Column | Type | Description |
|--------|------|-------------|
| `HistoryId` | `long`, identity, PK | Sequence within this history table |
| `Operation` | `string(1)` | `"I"`, `"U"`, or `"D"` (single-character string — `BinarySerializer` has no `char` primitive, so ledger payload compatibility drove the choice) |
| `ChangedOn` | `DateTime` (UTC) | Timestamp of the mutation |
| `ChangedBy` | `string` | Resolved from the ambient `ITrackingUserProvider` (default: `RequestIdentity.UserId`, falling back to `"system"` outside a request scope) |
| *(entity columns)* | — | Post-image of the row after the operation (includes the source PK — no separate `SourcePk` column needed) |

**Indexes:** AutoRepo provisions an index on the source entity's PK column(s) and on `ChangedOn` so "all history for row 42" and "changes between times T1 and T2" stay cheap. Composite PKs get a composite index.

**Post-image convention:** for INSERT and UPDATE the mirrored columns reflect the new state; for DELETE they reflect the last-known state (the row as it existed just before deletion). This makes "what did the row look like at time T?" a single query — take the most recent `_History` row matching the entity's PK with `ChangedOn <= T`.

### `{Table}_Ledger`

Provisioned by AutoRepo with a fixed, entity-independent shape:

| Column | Type | Description |
|--------|------|-------------|
| `LedgerId` | `long`, identity, PK | Chain position within this table |
| `HistoryId` | `long?`, unique-where-not-null | FK → `{Table}_History.HistoryId`. 1:1 with the paired history row for entity mutations; `NULL` for synthetic entries (prune markers, schema markers — see § Pruning and § Schema Drift). The `Operation` field inside `CanonicalBytes` disambiguates synthetic-row kinds (`'P'` prune, `'S'` schema). |
| `FormatVersion` | `byte` | Version of the canonical serialization format (currently `1`) |
| `CanonicalBytes` | `byte[]` | `BinarySerializer.Serialize(entityInstance)` — post-image, includes audit metadata |
| `PrevHash` | `byte[32]`, **unique** | `RowHash` of the previous row in this table's chain (all-zero for genesis). The `UNIQUE` constraint is load-bearing — see § Concurrency. |
| `RowHash` | `byte[32]` | `SHA256(FormatVersion ‖ HistoryId ‖ CanonicalBytes ‖ PrevHash)` — `HistoryId` contributes 8 big-endian bytes, or 8 zero bytes when `NULL`. |

All audit metadata (`ChangedOn`, `ChangedBy`, `Operation`) and every entity column live *inside* `CanonicalBytes` — covered by the hash. `HistoryId` is pulled *out* of the hashed blob and put directly into the hash input so that the ledger↔history pairing is itself integrity-protected; an attacker cannot swap `HistoryId` values across ledger rows without invalidating their hashes. The asymmetry is deliberate: `HistoryId` is a cross-table link and needs to be hash-covered *as a link*; the other audit fields are not links and are covered equivalently by living inside `CanonicalBytes`.

Navigation to a specific source row goes through `_History` (which mirrors the entity columns, including the PK) and then joins to `_Ledger` on `HistoryId`. The ledger row itself carries no entity-shape data as sibling columns, which is why this shape works for any PK type — int, long, GUID, string, or composite.

Chain head is derived directly: `SELECT RowHash FROM {Table}_Ledger WHERE LedgerId = (SELECT MAX(LedgerId) FROM {Table}_Ledger)`. No separate head-tracking table — the ledger is its own source of truth.

## Generic Entity Shape

History and ledger rows surface in C# as two generic types, one per tracked entity `T`:

```csharp
public class HistoryEntity<T> where T : class, new()
{
    public long     HistoryId  { get; set; }
    public char     Operation  { get; set; }
    public DateTime ChangedOn  { get; set; }
    public string   ChangedBy  { get; set; } = "";
    public T        Data       { get; set; } = new();
}

public class LedgerEntity<T> where T : class, new()
{
    public long   LedgerId       { get; set; }
    public long?  HistoryId      { get; set; }   // null for synthetic rows (e.g. prune markers)
    public byte   FormatVersion  { get; set; }
    public byte[] CanonicalBytes { get; set; } = [];
    public byte[] PrevHash       { get; set; } = [];
    public byte[] RowHash        { get; set; } = [];

    public T    Deserialize();       // lazy: BinarySerializer.Deserialize<T>(CanonicalBytes)
    public bool VerifySelfHash();    // recomputes SHA256(FV ‖ HistoryId ‖ CanonicalBytes ‖ PrevHash) and compares to RowHash
}
```

`HistoryEntity<T>` uses linq2db's [complex column mapping](https://linq2db.github.io/articles/project/Complex-Types.html) to flatten the nested `Data` object across the `_History` table's mirrored columns. AutoRepo registers each closed generic at startup via `FluentMappingBuilder`, reflecting `T`'s `[Column]` attributes and re-emitting them under the `Data.` path:

```csharp
// generated once per [Tracked] entity during startup
fb.Entity<HistoryEntity<Customer>>()
  .HasTableName("Customer_History")
  .Property(h => h.HistoryId).IsPrimaryKey().IsIdentity().HasColumnName("history_id")
  .Property(h => h.Operation).HasColumnName("operation")
  .Property(h => h.ChangedOn).HasColumnName("changed_on")
  .Property(h => h.ChangedBy).HasColumnName("changed_by")
  // for every [Column] on Customer (honoring [NotTracked]):
  .Property(h => h.Data.Id).HasColumnName("id")
  .Property(h => h.Data.Name).HasColumnName("name")
  .Property(h => h.Data.Email).HasColumnName("email")
  // ...
  .Build();
```

`ctx.GetTable<HistoryEntity<Customer>>().Where(h => h.Data.Email == "x")` then compiles to `WHERE email = 'x'` against `Customer_History`, and `h.Data` hydrates as a full `Customer` instance on read.

`LedgerEntity<T>` has a fixed shape — no flattening, no per-entity mapping needed. `T` is used only for table routing (`Customer_Ledger` → `LedgerEntity<Customer>`) and as the return type of `Deserialize()`.

> Programmatic `FluentMappingBuilder` registration on closed generics produces clean flat SQL for `Where` / `OrderBy` / `GroupBy` / joins / projections, and handles composite PKs, nullable value types, and inherited base-class columns without special cases. Built on linq2db 5.4.1.

**Constraints:**

- **No PK-type constraint on either side.** `HistoryEntity<T>` flattens `T`'s columns directly (including the PK), and `LedgerEntity<T>` links back via `HistoryId` rather than re-declaring any source-entity column. Composite keys, GUIDs, and strings all work for both `[Tracked]` and `[Ledger]`.
- **`T` needs a parameterless constructor** so linq2db can instantiate it during materialization. Already required for entities anyway.
- **`Data` is initialized to `new()` on the C# side.** The framework's write path always populates it from the source mutation; users are not expected to insert directly into `_History`.

## Write Path

Interception happens at AutoRepo's Insert / Update / Delete layer. Each mutation runs inside a transaction:

1. **For UPDATE / DELETE:** SELECT the target row(s) first. For bulk operations, materialize all affected rows into memory ordered by source PK.
2. **Apply the mutation** to the live table.
3. **For each affected row, in PK order:**
   1. Compute the post-image (for DELETE, reuse the pre-image since no post-image exists).
   2. If `[Tracked]`: append a `_History` row with mirrored columns + audit fields, capture the new `HistoryId`.
   3. If `[Ledger]` — retry loop (typically one iteration, capped at ~10):
      - `bytes = BinarySerializer.Serialize(postImageInstance)` — the instance includes audit metadata as properties.
      - `prev = SELECT RowHash FROM {Table}_Ledger WHERE LedgerId = (SELECT MAX(LedgerId) FROM {Table}_Ledger)` — returns `NULL` on an empty table, treated as 32 zero bytes (genesis).
      - `hash = SHA256(FormatVersion ‖ HistoryId ‖ bytes ‖ prev)`
      - Attempt `INSERT` `_Ledger` row `(HistoryId, FormatVersion, bytes, prev, hash)`.
      - On success → break.
      - On `UNIQUE` violation for `PrevHash` → another writer appended concurrently; loop and re-read `prev`. No savepoint needed — the failed `INSERT` is atomically rejected and the outer transaction stays alive (see § Concurrency).
      - If the retry cap is exhausted → throw `LedgerRetryExhaustedException`. Indicates catastrophic contention; surface to the caller.
4. Commit.

All history and ledger writes for a single user operation commit or roll back atomically with the source mutation. Retries never re-write the history row — its `HistoryId` is assigned once and reused across ledger-insert attempts.

### Bulk operations

`table.Where(x => x.Status == "archived").Update(x => new Order { Closed = true })` expands to:

1. Materialize affected row PKs inside the transaction.
2. Apply the bulk update.
3. Re-read the post-image of each affected row.
4. Append one history row + one ledger row per affected row, in PK order.

Cost is linear in affected-row count and unavoidable for chain correctness. Very high-cardinality bulk operations on `[Ledger]` tables are slow — a 100K-row status backfill on a ledgered table writes 100K ledger rows in one transaction, one serialized + hashed + head-read per row. For workloads like that, either chunk the update into smaller transactions, downgrade the table to `[Tracked]`, or accept the cost deliberately. `[Tracked]`-only is the right default for bulk-heavy tables unless integrity is specifically required.

Each per-row ledger insert runs its own unique-violation retry loop (see § Concurrency). A conflict on row 7 retries row 7 only, not the bulk — subsequent rows read their `PrevHash` from the current head after row 7 succeeds, so chain ordering stays correct. If any single row exhausts its retry cap, `LedgerRetryExhaustedException` is thrown and the entire transaction rolls back, including the source bulk mutation.

## Read Path

### `ctx.History<T>()`

Extension on `IAppDbContext`. Returns `IQueryable<HistoryEntity<T>>` pointed at `{Table}_History`. Audit metadata and entity data coexist on the same row via linq2db's complex column mapping (see § Generic Entity Shape):

```csharp
var recent = await ctx.History<Customer>()
    .Where(h => h.Data.Id == 42)
    .OrderByDescending(h => h.ChangedOn)
    .Take(10)
    .ToListAsync();

foreach (var h in recent)
{
    // h.Operation, h.ChangedOn, h.ChangedBy — audit metadata
    // h.Data.Name, h.Data.Email, ...         — entity post-image
}
```

No separate `HistoryLog` variant is needed — `HistoryEntity<T>` carries both surfaces.

### `ctx.Ledger<T>()`

Returns `IQueryable<LedgerEntity<T>>` over `{Table}_Ledger`. Used for chain navigation, metadata queries, and integrity verification. `LedgerEntity<T>` exposes `LedgerId`, `HistoryId`, `FormatVersion`, `CanonicalBytes`, `PrevHash`, `RowHash`, and a lazy `Deserialize()` method that materializes `T` from `CanonicalBytes` on demand.

Navigation to a specific source row joins through `_History` (which carries the mirrored PK column):

```csharp
var versions = await (
    from h in ctx.History<Customer>()
    where h.Data.Id == 42
    join l in ctx.Ledger<Customer>() on h.HistoryId equals l.HistoryId
    orderby l.LedgerId
    select l
).ToListAsync();

var latest = versions[^1].Deserialize();   // Customer instance at that point
```

### `ctx.Verify<T>(...)`

Integrity check. **Verification is forward-walk and chain-wide** — it is not scoped to a particular source row. The chain is a single linear structure across all of a table's mutations; rows for different source entities are interleaved. Verifying a PK-filtered subset would either skip forward-link checks (weaker, confusing) or verify the whole chain anyway (cosmetic scoping). Neither is worth offering. If a caller wants to inspect versions of a specific source row, that's a `ctx.History<T>()` / `ctx.Ledger<T>()` query, not a verification concern.

Starting from the oldest surviving ledger row and proceeding by ascending `LedgerId`, for each row:

- Validate the self-hash: `RowHash == SHA256(FormatVersion ‖ HistoryId ‖ CanonicalBytes ‖ PrevHash)`.
- Validate the forward link to the next row: `next.PrevHash == this.RowHash`.

The oldest surviving row's `PrevHash` may reference a pruned ancestor (see § Pruning). Forward-walk never checks that backward link — it only appears as an input to the row's own self-hash, which is validated. This is why tail pruning is integrity-safe.

Two call shapes:

```csharp
// Internal consistency only — "does the chain still hash correctly?"
ctx.Verify<T>()                                    // -> VerificationResult

// Anchored — "has anything changed since this externally-captured digest?"
ctx.Verify<T>(LedgerDigest anchor)                 // -> VerificationResult
ctx.Verify<T>(IEnumerable<LedgerDigest> anchors)   // -> VerificationResult
```

For spot-checking a specific row's self-hash without walking the chain, use `LedgerEntity<T>.VerifySelfHash()` — an instance method that recomputes `SHA256(FormatVersion ‖ HistoryId ‖ CanonicalBytes ‖ PrevHash)` from fields already on the instance and compares to the stored `RowHash`. Pure CPU work, no DB roundtrip, does not validate forward links.

Internal consistency checks catch broken forward-links, mismatched self-hashes, and reordered rows. They do **not** catch consistent cascade rewrites (a privileged user who rewrites from row N forward, updating every hash along the way) or tail truncation. Those are detected only against an external anchor — see § Digests & Anchoring.

Return type:

```csharp
public sealed class VerificationResult
{
    public bool Valid { get; init; }

    // Internal chain consistency
    public bool InternalConsistencyValid { get; init; }
    public long? FirstBadLedgerId { get; init; }
    public VerificationFailure? FailureKind { get; init; }
    public long EntriesVerified { get; init; }

    // Anchor verification (populated only when anchors were provided)
    public int AnchorsChecked { get; init; }
    public int AnchorsMatched { get; init; }
    public int AnchorsSuperseded { get; init; }    // row covered by a later prune event — anchor is historical, not failed
    public IReadOnlyList<AnchorFailure> AnchorFailures { get; init; } = [];
    public DateTime? LastKnownGood { get; init; }   // newest anchor that matched
    public DateTime? EarliestTamper { get; init; }  // oldest anchor that failed
}

public enum VerificationFailure
{
    RowHashMismatch,   // stored RowHash != recomputed hash
    PrevHashBroken,    // forward link broken: next.PrevHash != this.RowHash
}

public sealed class AnchorFailure
{
    public DateTime AnchorTime     { get; init; }
    public long     AnchorLedgerId { get; init; }
    public byte[]   ExpectedHash   { get; init; } = [];
    public byte[]?  ActualHash     { get; init; }
    public AnchorFailureKind Kind  { get; init; }
}

public enum AnchorFailureKind
{
    RowHashMismatch,   // row exists, its hash differs from the anchor's — cascade rewrite
    RowMissing,        // row is gone with no covering prune event — truncation or delete
    ChainRegression,   // current MAX(LedgerId) is below the anchor's LedgerId — rollback
}
```

### `sp_entity_history`

System procedure for generic / admin-console access:

```
sp_entity_history(table, pk, limit?=100, offset?=0)
→ { Items: [{ HistoryId, Operation, ChangedOn, ChangedBy, State }], Total }
```

Backs the admin console's History tab; gives non-LINQ callers a generic entry point.

## Canonical Bytes

`CanonicalBytes = BinarySerializer.Serialize(entityInstance)` with default options. The instance written to the ledger is a thin wrapper that carries the post-image column values plus audit fields (`ChangedOn`, `ChangedBy`, `Operation`) as regular properties — ensuring they are covered by the hash.

**Determinism of serialization output is not a requirement.** Integrity verification re-hashes the exact stored bytes; it never re-serializes from live data. Two different-but-valid serializations of the same entity would both verify correctly against their own stored hashes. What *is* required is that the deserializer can read old bytes forever — a constraint SmartData already honors for RPC wire-format compatibility.

`FormatVersion` is a 1-byte prefix to the hash input. Currently `1`. If a future major release makes a binary-incompatible change to `BinarySerializer`, bump to `2`; readers dispatch on this byte; old rows continue to verify under the old rules.

## Hash Chain

One chain per ledgered table. Per-table (not global) keeps concurrent writes across different ledgered tables independent. Genesis row's `PrevHash` is 32 zero bytes. The chain head is the row with the maximum `LedgerId` — derived on read, never cached.

Verification is forward-walk from the oldest surviving row to the head (see § Read Path → `ctx.Verify<T>`). The backward link from the first surviving row to a possibly-pruned predecessor is never checked, only hashed-over as an input to the row's own self-hash.

### Hash input encoding

```
RowHash = SHA256( FV ‖ HID ‖ CB ‖ PH )

  FV   FormatVersion        1 byte
  HID  HistoryId            8 bytes, big-endian; 0x00…00 when NULL
  CB   CanonicalBytes       variable length
  PH   PrevHash             32 bytes; 0x00…00 at genesis
```

No separators, no length prefixes, no framing. The hash encoding itself is not self-delimiting — SHA-256 does not record input length. What makes this unambiguous is that the verifier reads `CanonicalBytes` from the row as a sized `byte[]`, so `CB`'s length is always known at hash time; the fixed widths of `FV`, `HID`, and `PH` then pin their positions in the input stream.

**Invariant: exactly one variable-length field.** This is load-bearing for collision resistance without a length prefix. A future contributor who adds a second variable field to the hash input must introduce explicit length framing at the same time, or the encoding becomes ambiguous. Bump `FormatVersion` when doing so.

Alternate-language verifiers (external auditor scripts, future port to another runtime) MUST reproduce these widths and byte order exactly. `HistoryId` is **big-endian** regardless of host architecture.

## Schema Evolution

Handled by AutoRepo's migration diff with two rules:

- **`_History`:** schema mirrors the source entity. Added columns appear as `NULL` for pre-existing history rows. Dropped columns are removed from new writes; historical values of dropped columns are **lost from `_History`** but **preserved in `_Ledger.CanonicalBytes`** for ledgered entities.
- **`_Ledger`:** schema is fixed. New entity columns are automatically captured in `CanonicalBytes` for new rows (they don't appear in old rows — that's correct). Dropped columns simply stop being captured for new rows. Type changes apply only to new rows.

All of this works because `CanonicalBytes` is a schema-less self-describing blob. A row written in 2026 with 5 columns still verifies and deserializes in 2030 after 3 more columns were added to the source entity.

## Lifecycle

Tracking is **sticky**: once `[Tracked]` or `[Ledger]` is applied and the corresponding tables exist, removing the attribute does **not** stop tracking. Silently losing audit coverage because of a refactor, a merge, or accidental attribute deletion would be the worst kind of default — so the framework treats *table existence* as the persistent record of intent. The attribute says what should be enabled; the table says what has been enabled.

### Startup resolution

For each entity, startup resolves a tracking mode using both the attribute and whether the corresponding tables already exist:

| Attribute | `_History` | `_Ledger` | Resolved mode | Behavior |
|-----------|:----------:|:---------:|---------------|----------|
| `[Ledger]`  | —  | —  | Ledger  | Provision both tables; first write is genesis |
| `[Ledger]`  | ✓ | —  | Ledger  | **Upgrade** — provision `_Ledger`; ledger chain begins at genesis |
| `[Ledger]`  | ✓ | ✓ | Ledger  | Normal operation |
| `[Tracked]` | —  | —  | Tracked | Provision `_History` |
| `[Tracked]` | ✓ | —  | Tracked | Normal operation |
| `[Tracked]` | ✓ | ✓ | **Ledger**  | Tables override attribute downgrade — continue ledgering, warn |
| *(none)*    | —  | —  | None    | No tracking |
| *(none)*    | ✓ | —  | **Tracked** | Attribute removed, continue tracking, warn |
| *(none)*    | ✓ | ✓ | **Ledger**  | Attribute removed, continue ledgering, warn |

**Rule: table existence always wins over attribute absence.** Absence is never interpreted as intent to disable.

When attributes and tables disagree (any row above with a bolded resolution), startup logs:

```
[WARN] Entity 'Customer' has no [Tracked]/[Ledger] attribute, but
       Customer_History exists. Continuing in Tracked mode. To stop,
       call sp_tracking_drop(table='Customer').
```

Loud enough to notice in a code review; quiet enough that production keeps running.

### Column-level policy during sticky mode

`[NotTracked]` continues to apply to whatever properties remain. If you remove `[Tracked]` from the class but keep `[NotTracked]` on `PasswordHash`, the exclusion still holds. Only the top-level declaration goes sticky; fine-grained column policy still comes from property attributes.

### Disabling tracking

Two explicit procedures, depending on how far down you want to go. Both require `confirm == tableName` (double-typing guard against accidents). **The `confirm` check is enforced inside the procedure body, not in the CLI wrapper** — a developer calling `ctx.CallProcedure("sp_tracking_drop", …)` from code hits the same guard as a CLI invocation. A typo-protection guard that lived only in argparse wouldn't actually be a guard. Neither procedure is reversible from the framework's perspective.

#### `sp_ledger_drop(table, confirm)` — downgrade Ledger → Tracked

Drops `_Ledger` only. `_History` remains, writes continue to append to it, the entity keeps its queryable history, and it loses integrity going forward.

**Pre-condition:** the developer must change `[Ledger]` → `[Tracked]` (or remove the attribute, which goes sticky-tracked) *before or alongside* the drop. Otherwise the sticky-resolution table (above) sees `[Ledger]` + `_History` ✓ + `_Ledger` — at next startup and re-provisions `_Ledger` with a fresh genesis row.

**Consequences:**

- All prior integrity claims for this table are permanently void.
- Existing JSONL digests become unverifiable — the chain they anchor no longer exists. Retain them as historical artifacts only.
- Pruned-data archives (`sp_ledger_prune` output) similarly lose their chain context.
- If `[Ledger]` is later re-added, the new chain starts at genesis with a fresh schema marker. It does **not** claim continuity with the dropped chain.

There is no corresponding "drop history only" procedure. `[Ledger]` without `_History` is unreachable in the sticky-resolution table, and `_Ledger` alone has no human query surface.

#### `sp_tracking_drop(table, confirm)` — full removal

Drops both `_History` and `_Ledger`. All queryable history is gone; all ledger integrity claims are permanently void. JSONL digest history and `sp_ledger_prune` archives should be retained separately if the data may later need to be defended.

### Orphan tables

If the entity *type* is removed from the code (not just its attribute), `_History` and `_Ledger` become orphaned — nothing is writing to them (entity API is gone) and nothing drops them either. Tables persist until explicit `sp_tracking_drop`. AutoRepo does not auto-clean unknown tables; that's the correct default for migration safety.

### Retroactive hashing is not supported

Upgrading `[Tracked]` → `[Ledger]` does not retroactively hash pre-existing `_History` rows. The new ledger chain starts at genesis on the first post-upgrade write — and that genesis row is the initial schema marker (see § Schema Drift → Genesis), establishing the captured-set baseline for the new chain. Pre-upgrade history rows remain queryable but unverifiable. Anyone who needs retroactive integrity must ledger from day one.

## `[NotTracked]` Semantics

Marks a property for exclusion from both `_History` mirrored columns and `_Ledger.CanonicalBytes` (via `[BinaryIgnore]` on the ledger serialization wrapper). Typical use: secrets, PII, large denormalized blobs.

**Captured-set changes are detected and recorded, not silent.** Adding or removing `[NotTracked]` mid-life changes the set of fields captured — old rows and new rows have different canonical content. Each row still self-verifies, but an auditor reading two rows separated by the policy change would have no way to see that the rules shifted between them. The framework closes this by writing a chained schema marker whenever the captured set drifts. See § Schema Drift for the mechanism.

## Concurrency

The chain is serialized by the DB's own uniqueness machinery, not by an application-level lock. The `UNIQUE` constraint on `_Ledger.PrevHash` is the key mechanism: a linear chain has the property that each row's hash is referenced as `PrevHash` by exactly one child, so enforcing uniqueness at the schema level makes forking structurally impossible.

Concurrent writers race as follows:

1. Both read `MAX(LedgerId)` and its `RowHash` (call it `H_N`).
2. Both compute their own row hashed with `PrevHash = H_N`.
3. Both attempt `INSERT`.
4. The database accepts exactly one. The other's `INSERT` fails with a `UNIQUE` violation on `PrevHash`.
5. The loser re-reads head (now including the winner's row), recomputes its own `RowHash` against the new `PrevHash`, and retries.

Retry cost: one `SELECT` + one `INSERT`. Microseconds. `CanonicalBytes` is independent of chain state so it doesn't need to be re-serialized. The history row written earlier in the transaction keeps its `HistoryId` — retry only re-runs the ledger `INSERT`.

**Why this beats the alternatives:**

- **No `SysLedgerHead` table.** The ledger is its own source of truth; no denormalized head to keep in sync.
- **No provider-specific lock API.** Unique-index enforcement is universal across SQLite, SQL Server, and any future provider. Zero dialect branching.
- **No lock ownership state.** The DB owns arbitration; the app just retries on conflict.
- **Forks are structurally impossible.** With a lock-based design, a bug in the lock-update code could silently produce a forked chain. With `UNIQUE` on `PrevHash`, the DB rejects the second-to-commit row before it touches the table.

Writes to different ledgered tables never contend — each table has its own `_Ledger.PrevHash` unique index.

`[Tracked]`-only tables have no chain and no uniqueness constraint on history rows — writes proceed with only the normal DB-level locking on the `_History` insert.

SQLite keeps the outer transaction usable after a `UNIQUE`-violation `INSERT` unconditionally. SQL Server does the same under `XACT_ABORT OFF`; under `XACT_ABORT ON` it rolls the whole transaction back immediately (`XACT_STATE() = 0`, preceding writes gone). The SQL Server provider enforces `XACT_ABORT OFF` at connection open — see § Dependencies.

## Dependencies

- **`IAppDbContext.CurrentUser`** — currently hardcoded `"system"` (see `SmartApp.Backend/Data/AppDbContext.cs`). The integrity value of `ChangedBy` is only as trustworthy as this property. Wiring it to the authenticated session is a prerequisite for meaningful audit but is **out of scope for this feature** and tracked separately.
- **`BinarySerializer`** — used for `CanonicalBytes`. The "bytes readable forever" constraint this feature adds aligns with the wire-format compat constraint already required for RPC. No new discipline.
- **AutoRepo migration diff** — must be extended to provision `_History` and `_Ledger` tables (including the `UNIQUE` index on `_Ledger.PrevHash` and the unique-where-not-null index on `_Ledger.HistoryId`) and to handle column add/drop on `_History`.
- **Provider support for filtered / partial unique indexes.** The `HistoryId` uniqueness constraint is "unique where not null" — SQLite expresses this as `CREATE UNIQUE INDEX … WHERE HistoryId IS NOT NULL`, SQL Server as a filtered index with the same predicate. Both currently-shipped providers support it; any future provider must before it can host a ledgered table.
- **SQL Server transaction semantics under `XACT_ABORT`.** The lock-free concurrency design assumes the outer transaction stays usable after a `UNIQUE(PrevHash)` violation on `INSERT`. SQLite preserves the transaction on constraint errors unconditionally. SQL Server does the same *unless* `XACT_ABORT ON` is set at the session level, which forces a transaction-wide rollback and erases everything in the same transaction. `SqlServerDatabaseProvider.OpenConnection` issues `SET XACT_ABORT OFF` on every connection open to enforce this; callers must not override the setting.

## Pruning

Ledger entries grow linearly with writes. Two procedures handle pruning, split by what each table is for.

### `sp_history_prune(table, olderThan)`

For `[Tracked]`-only tables. Plain `DELETE`:

```
DELETE FROM {Table}_History WHERE ChangedOn < olderThan
```

Safe, idempotent, provider-neutral. **Refused on `[Ledger]` tables** — the two tables must prune together, so use `sp_ledger_prune` instead.

### `sp_ledger_prune(table, olderThan, archive? = null)`

For `[Ledger]` tables. Prunes both the ledger and the paired history rows in one transaction, records the prune itself as a synthetic ledger row, and optionally archives pruned data first.

Sequence:

1. Find `boundary = MAX(LedgerId)` among ledger rows whose paired history row has `ChangedOn < olderThan`.
2. Verify internal chain consistency up to `boundary` (fail fast on pre-existing corruption).
3. If `archive` is set, export rows `1..boundary` to the archive destination before deleting.
4. **Insert a synthetic prune-marker ledger row** (same retry-on-`UNIQUE`-violation loop as a normal write; shares the `HistoryId IS NULL` convention with schema markers — see § Schema Drift):
   - `HistoryId = NULL` (no paired history row)
   - `CanonicalBytes` = serialized prune metadata: `{ Operation='P', PrunedFrom=1, PrunedTo=boundary, BoundaryRowHash=RowHash(boundary), PrunedAt=now, PrunedBy=user, ArchiveRef }`
   - `PrevHash` = current chain head's `RowHash`
   - `RowHash` computed normally; since the marker is chained like any other row, a future anchor covers it automatically.
5. `DELETE FROM {Table}_Ledger WHERE LedgerId <= boundary`. All rows in the pruned range go, **including schema markers** — preserving them would leave the chain with non-contiguous survivors, and forward-walk verification requires each surviving row's `PrevHash` to equal the previous surviving row's `RowHash`. That invariant holds only when deletion is a contiguous prefix. The prune marker itself is the chain's new "oldest surviving" row — no other rows at `LedgerId ≤ boundary` survive. Historical captured-set info for the pruned window lives in the prune marker's metadata and in the archive blob (if one was supplied).
6. `DELETE FROM {Table}_History WHERE HistoryId` ∈ the set paired to pruned ledger rows.
7. Commit.

Why forward-walk makes this safe: after pruning, the oldest surviving row is the prune marker. Its `PrevHash` references the vanished boundary row, but that reference is never checked as a backward link — it's only an input to the marker's own self-hash, which is validated normally. The post-prune chain walks forward from the marker through surviving rows with every link intact.

### What pruning does and does not erase

- **Erases:** queryable history of pruned rows from `_History`. Bytes of pruned ledger rows from `_Ledger` (content is gone unless archived).
- **Preserves:** the *fact* that rows existed in a given LedgerId range, the *hash* of where the chain was at the prune boundary, the *identity* of who pruned and when — all captured in the prune marker row, which is itself hash-chained.

### Anchoring cadence vs. prune cadence

**Anchor at least as often as you prune.** If you prune rows older than 90 days on a monthly cadence, publish a digest at least monthly — ideally just before each prune. Otherwise the window between your last anchor and the prune is unprotected: a privileged user could quietly tamper with rows about to disappear, and the prune erases the evidence along with whatever they rewrote.

See § Digests & Anchoring for the capture surface.

## Schema Drift

Two ledger rows can both self-verify and yet mean different things, because the set of captured columns changed between them. A property's `[NotTracked]` attribute is added or removed, a column is added or dropped on the source entity, or a property's type changes — each shifts what `CanonicalBytes` actually contains. Individual rows still hash correctly, but an auditor comparing row #341,000 to row #342,000 has no way to see that the *rules* shifted in between. For a medical-grade audit claim, that silent shift is the exact failure mode the ledger is supposed to prevent.

The framework closes this by writing a **schema marker** into the ledger chain whenever the captured set changes. The chain itself is the source of truth for what policy was in effect at any point — no sidecar table, no denormalized stamp. If you trust the chain, you trust the drift history; if you don't, anchoring (§ Digests & Anchoring) is the defense for both.

### Captured set

For a given ledgered entity `T`, the **captured set** is the sorted list of `(ColumnName, ClrType.FullName)` pairs produced by:

1. Reflect every `[Column]`-attributed property on `T`, including inherited properties from base classes.
2. Exclude any property marked `[NotTracked]`.
3. Sort by column name, ascending.
4. Canonicalize as UTF-8 text (`name:type\n` per entry). Compute `CapturedHash = SHA256(canonical bytes)`.

This is the fingerprint that "drift" is defined against. Renames, type changes, additions, removals, and `[NotTracked]` toggles all register. Column order in the source entity does not — sort order is fixed. `AutoRepoVersion` and other build metadata are *not* part of the fingerprint, so redeploying the same code does not trip drift.

### Marker shape

Schema markers share the synthetic-row convention already established by prune markers (§ Pruning):

| Field | Value |
|-------|-------|
| `LedgerId` | Next chain position, assigned by identity |
| `HistoryId` | `NULL` |
| `PrevHash` | Current chain head's `RowHash` |
| `RowHash` | Normal: `SHA256(FormatVersion ‖ HistoryId ‖ CanonicalBytes ‖ PrevHash)` — `HistoryId` contributes 8 zero bytes since it's `NULL` |
| `CanonicalBytes` | `BinarySerializer.Serialize(SchemaMarker { … })` |

`SchemaMarker` payload:

```csharp
public sealed class SchemaMarker
{
    public char        Operation       { get; set; } = 'S';
    public DateTime    DetectedAt      { get; set; }
    public string      DetectedBy      { get; set; } = "";   // IAppDbContext.CurrentUser at startup
    public byte[]      CapturedHash    { get; set; } = [];   // 32 bytes
    public CapturedColumn[] Columns    { get; set; } = [];   // current captured set, sorted
    public string[]    Added           { get; set; } = [];   // columns added vs previous marker (empty at genesis)
    public string[]    Removed         { get; set; } = [];   // columns removed vs previous marker (empty at genesis)
    public string?     AutoRepoVersion { get; set; }         // informational — not part of the fingerprint
}

public sealed class CapturedColumn
{
    public string Name    { get; set; } = "";
    public string ClrType { get; set; } = "";
}
```

The marker is chained like any other row. Any future digest covers it automatically; verification walks through it without special handling — it simply self-hashes and forward-links correctly.

### Detection

At startup, for each `[Ledger]` entity, before the first entity write in that session:

1. Compute the current captured set (reflection over `T`).
2. Read the most recent synthetic row for this table:
   ```
   SELECT LedgerId, CanonicalBytes
   FROM {Table}_Ledger
   WHERE HistoryId IS NULL
   ORDER BY LedgerId DESC
   LIMIT 1
   ```
   Deserialize and dispatch on `Operation`:
   - `'S'` — compare `CapturedHash` against the current set.
   - `'P'` (prune marker) — walk one step further back (`LedgerId < marker.LedgerId`) to find the preceding `'S'`. Prune markers do not record policy, so they are skipped for drift comparison. In practice there is always an `'S'` earlier in the chain because genesis writes one (below).
   - No synthetic row at all — table has no genesis marker; this is the genesis case (below).
3. If the hashes match: done, no marker written.
4. If they differ: compute `Added` / `Removed` from the symmetric diff and write a new `'S'` marker using the same retry-on-`UNIQUE`-violation loop as any other ledger insert. The marker commits in its own transaction, before any entity writes in this startup session.

Cost: one indexed lookup + one deserialize per ledgered table per startup. No hot-path impact.

#### Concurrent startup across instances

When multiple app instances boot against the same DB, each reaches the drift check independently. Resolution piggybacks on the normal `UNIQUE(PrevHash)` retry loop (§ Concurrency), with one extra guard on the loss path:

1. Winner's marker `INSERT` succeeds.
2. Loser's `INSERT` fails on `UNIQUE(PrevHash)` and re-reads head.
3. **Before recomputing and retrying, the loser re-runs detection step 2** — read the most recent `'S'` marker. If its `CapturedHash` now matches the current reflection, the winner already wrote our marker; skip silently.
4. Otherwise retry as normal. This path is rare — it means two instances observed genuinely different captured sets (e.g. mid-rollout with mixed builds) and the second transition is worth recording.

The same guard applies to genesis: loser of the all-zero-`PrevHash` race re-reads, sees a genesis marker already present, and skips. Comparison is by `CapturedHash`, not row identity, so two instances computing the same new set deduplicate correctly.

### Genesis

**A brand-new `[Ledger]` table writes a schema marker as its first ledger row — row #1, before any entity writes.** This establishes an explicit baseline: row #1 always records "here is what was captured at the moment this ledger began." No ambiguity about "what did row #1 mean" for an auditor reading years later, and later drift detections have something unambiguous to diff against.

Genesis marker `Added` and `Removed` are both empty; `Columns` carries the full initial set.

### Retroactive markers

If a deploy rolls out with drift but no marker is ever written (upgrade from a pre-drift-detection build, or a bug that skipped detection), the next startup that does detect drift writes **one** marker comparing the most recent prior marker to the current reflection. `DetectedAt` is that startup's timestamp, not when the policy actually changed — and that is honest. Anchoring cadence defines the tightest window within which the drift *must* have occurred; narrowing it further requires external evidence, not an after-the-fact marker backdated by the framework.

### `[Tracked]`-only tables

No chain exists to write a marker into. The framework provisions a `SysTrackedColumns(TableName, CapturedColumnsJson, CapturedHash, StampedOn, StampedBy)` sidecar table. At startup, for each `[Tracked]`-only entity:

1. Compute the current captured set.
2. Compare against the sidecar row for this table.
3. On drift: log a WARN with added/removed columns and update the sidecar.
4. On first sight: insert the baseline row.

The sidecar is *only* a detection aid. It carries no integrity claim — a `[Tracked]`-only table has none to begin with. The value is operator-visible drift logging, nothing more. Do not treat `SysTrackedColumns` as an audit surface; it is mutable and unchained by design.

The admin console's Schema History tab **must visually distinguish** ledger-backed timelines (chained `'S'` markers, integrity-protected) from sidecar-backed timelines (mutable rows in `SysTrackedColumns`). An operator inspecting a `[Tracked]`-only table's schema history should never be able to mistake it for an integrity claim — render it under a clearly different badge ("Drift log", not "Schema ledger") and omit hash fields entirely.

### Admin console & CLI

- `ctx.SchemaMarkers<T>()` — extension on `IAppDbContext`, returns an `IQueryable<SchemaMarker>` by reading `_Ledger` synthetic rows and filtering to `Operation='S'`. For `[Tracked]`-only tables, query `SysTrackedColumns` directly.
- `sp_schema_history(table)` — system procedure returning the full marker timeline for a table (ledger for `[Ledger]`, sidecar for `[Tracked]`).
- Admin console's Ledger tab renders schema markers with a distinct "Schema change" badge and an Added / Removed diff (see § Admin Console Integration).

### What this catches

| Change | Detected | Marker written |
|--------|:--------:|:--------------:|
| `[NotTracked]` added to a property | ✓ | ✓ |
| `[NotTracked]` removed from a property | ✓ | ✓ |
| New `[Column]` property added to `T` | ✓ | ✓ |
| `[Column]` property removed from `T` | ✓ | ✓ |
| Property's CLR type changed | ✓ | ✓ |
| `[Column(Name=…)]` renamed | ✓ | ✓ |
| Column order in source class rearranged | ✗ (captured set is sorted) | — |
| Redeploy of identical code | ✗ (hash unchanged) | — |

Each detected change produces exactly one marker, regardless of how many columns shifted in that change. The marker's `Added`/`Removed` arrays carry the full diff.

## Digests & Anchoring

Internal chain verification catches inconsistencies (broken forward-links, mismatched self-hashes, reordering) but cannot detect a **consistent cascade rewrite** — a privileged user who rewrites row N, then updates every `PrevHash` and `RowHash` from N to the head. The chain becomes internally perfect; only something outside the database remembering an earlier state can catch it.

That's what anchoring is: periodically capture the current chain head's digest to a location the DBA cannot rewrite (signed receipt, immutable blob storage with retention lock, a third-party ledger, printed and filed). The framework provides capture and verification helpers; the external store and the publishing workflow are operational responsibilities.

### `sp_ledger_digest(table)`

Returns the current chain-head state — the exact payload meant to be anchored:

```
sp_ledger_digest(table)
→ LedgerDigest {
    TableName,
    LatestLedgerId,   // MAX(LedgerId)
    LatestRowHash,    // RowHash at that position
    ChangedOn,        // timestamp of the corresponding history row (or the prune marker's time, for synthetic rows)
    EntryCount        // COUNT(*) — informational, not authoritative
  }
```

**Recommended wire format: JSONL** (one JSON object per line, UTF-8, LF-terminated). Fields match `LedgerDigest` above; `LatestRowHash` is hex-encoded. JSONL was chosen deliberately over single-JSON-per-file:

- **Append-only friendly.** `sp_ledger_digest` output is piped with `>>`. No rewrite of existing bytes — minimizes the attack surface against the store itself (an immutable-append blob policy is strictly enforceable).
- **Naturally holds a sequence of anchors over time.** The anchored verify API (`ctx.Verify<T>(IEnumerable<LedgerDigest>)`) consumes the whole file as a chronological anchor history, which is exactly the shape needed for binary-searching a tamper window.
- **Trivially tailable.** Single-anchor verify reads the last line; no parser state, no schema evolution.

Example line:

```json
{"TableName":"Customers","LatestLedgerId":15342,"LatestRowHash":"a3f1...","ChangedOn":"2026-04-21T03:00:04Z","EntryCount":15342}
```

Minimal operational loop:

```
# hourly cron — append a digest
sd call sp_ledger_digest table=Customers >> digests/customers.jsonl

# spot check against the most recent anchor
sd call sp_ledger_verify table=Customers anchor=@digests/customers.jsonl:tail

# full-history verify — catches intermediate tampering and produces a LastKnownGood / EarliestTamper window
sd call sp_ledger_verify table=Customers anchors=@digests/customers.jsonl
```

Rotation is operational: when a JSONL file gets large, archive it (its last line becomes the first line of the successor) and pin the archive in immutable storage. The file format itself imposes no size limit.

### `sp_ledger_verify(table, anchor?)`

Server-side integrity check. Without an `anchor`, runs the internal forward-walk. With a digest parameter, also validates that the current chain is consistent with the anchored head (hash match at `AnchorLedgerId`, no unexplained truncation or regression). Returns the same `VerificationResult` shape described in § Read Path → `ctx.Verify<T>`.

### Background verification

Pair with the existing scheduling subsystem — no new infrastructure:

```csharp
[Daily(Hour = 3)]
public class LedgerVerifyDaily : AppProcedure<VoidResult>
{
    public override VoidResult Execute(IAppDbContext ctx, CancellationToken ct)
    {
        foreach (var tableName in ctx.EnumerateLedgeredTables())
            ctx.CallProcedure("sp_ledger_verify", new { table = tableName });
        return VoidResult.Instance;
    }
}
```

Outcomes land in `SysScheduleRun` like any other scheduled procedure. Admin dashboards and monitoring read from there — no dedicated ledger-health table needed.

### What anchoring buys

| Tampering | Caught by chain alone | Caught by chain + anchor |
|-----------|----------------------|--------------------------|
| Single-row edit without cascade | ✓ | ✓ |
| Row reordering / mid-chain delete | ✓ | ✓ |
| Consistent cascade rewrite | ✗ | ✓ |
| Tail truncation | ✗ | ✓ |
| Rollback to earlier state | ✗ | ✓ |
| Forged prune event (no matching external record) | ✗ | ✓ |

Without anchoring: the chain makes low-effort tampering impossible and high-effort tampering visibly expensive, but a determined adversary with DB write access can still produce an internally-consistent rewrite. With anchoring: the chain + digest together provide tamper-evidence proportional to anchor cadence. The shorter the interval between anchors, the smaller the window of deniability.

A sequence of stored digests enables binary-search narrowing of a tamper window: find the most recent anchor that verifies and the oldest that fails; the breach lies between them.

## Operational Concerns

### Restore from backup

Restoring the database from backup preserves ledger integrity *within the restored data* — the chain comes back whole, every row still self-hashes and forward-links correctly. What it does not preserve is consistency with anchors captured *after* the backup was taken: those anchors reference `LedgerId` values and `RowHash`es that no longer match the restored chain, and will fail verification with `ChainRegression` or `RowHashMismatch`.

This is intentional. From an anchor's perspective, restore is indistinguishable from a rollback-style tamper event, and an external auditor should not have to trust an operator's assurance that "this was a real restore, not a rewrite." The correct post-restore procedure:

1. Stop writers.
2. Verify internal chain consistency against the restored DB.
3. Capture a fresh `sp_ledger_digest` and publish it to the anchor store as the new post-restore baseline.
4. Retain the pre-restore digest history as a separate archive — it attests to the pre-restore chain, which still existed at some point, and may be useful for forensics.

Re-anchor immediately after every restore. A restored DB with no fresh anchor is a ledger with a blind window.

### Multi-database deployments

SmartData's `UseDatabase` allows a single server to host multiple logical databases. Tracking is **per-database-per-table**: each database file has its own `{Table}_History` and `{Table}_Ledger`, its own chain, its own genesis, and its own anchors. There is no cross-database ledger, no shared chain, and no cross-database drift detection.

`AppProcedure<T>.Database` (the base-class override) determines which DB's ledger a given procedure's writes land in. If two databases host an entity with the same table name, they have two independent ledgers — verifying one says nothing about the other. Anchor each database's tables separately.

### Performance budget

Ceilings the implementation is held to — anything worse is a bug, not a tuning opportunity. These are not benchmarked measurements yet; concrete numbers belong here once an end-to-end benchmark exists.

- **Single-row ledgered write, uncontended:** within 4× a plain history insert on the same row.
- **Single-row ledgered write, head-of-chain contention (2 writers):** within 8× a plain history insert, counting the one expected retry.
- **Startup schema-drift check:** one indexed lookup + one deserialize per ledgered table. Total added startup time for 100 ledgered tables should be well under one second.
- **Verification throughput:** `ctx.Verify<T>()` over a cold cache should walk at least 50K ledger rows/second on SQLite with default hardware. Below that suggests hashing or deserialization is mis-shaped.

## Limitations

### Raw SQL bypass

Mutations via raw SQL, direct `IDbConnection` usage, or DBA-level access do not go through AutoRepo's interceptor and are invisible to both history and ledger. The integrity guarantee applies only to operations through AutoRepo / linq2db entity APIs. Document as a trust-boundary carve-out.

### Bitemporal queries

`ctx.AsOf<T>(DateTime)` returning the full row state at a point in time is not implemented. Emulate by taking the most recent `_History` row matching the entity's PK with `ChangedOn <= T` — that's what the post-image convention (§ Storage) is designed for. A dedicated API would need `ValidFrom` / `ValidTo` columns on `_History` and careful handling of concurrent mutations.

### Throughput on ledgered writes

Every write pays for serialization, hashing, and the head-read for `PrevHash`. Under contention, retries add to the cost. Rough estimate — not yet benchmarked — is ~2–4× a plain history insert, with worst-case tail latency proportional to contention. Opt-in per table is the mitigation — use `[Ledger]` only where integrity is actually required.

### Storage cost

Rough model for capacity planning:

- **`_History`** per row ≈ source entity row size + ~40 bytes of audit metadata.
- **`_Ledger`** per row ≈ `CanonicalBytes` size (typically 10–20% larger than the source columns due to type markers) + 72 bytes of fixed overhead (`LedgerId`, `HistoryId`, `FormatVersion`, `PrevHash`, `RowHash`).

For an entity averaging 1 KB of column data at 10 K writes/day over 1 year:

- `[Tracked]` alone: ≈ 3.6 GB/year.
- `[Ledger]`: ≈ 7.3 GB/year across both tables combined.

`sp_history_prune` / `sp_ledger_prune` are the primary growth controls. Anchor digests are tiny and independent of row volume — a year of hourly digests is a few MB at most.

## Error Handling

The ledger's entire value is that it is trustworthy. A library that silently recovers from its own errors — patching hashes, dropping bad rows, degrading to tracked-only without being told — destroys that value. The framework defaults to **fail loud, never silently**: typed exceptions, no auto-repair, no degradation on error. The developer decides whether to retry, abort the user-facing operation, page oncall, or quarantine the table.

### Rules

1. **Write-path errors abort the source mutation.** Any failure in serialization, hashing, or ledger insertion — other than the expected `UNIQUE(PrevHash)` race, which is retried automatically — rolls back the outer transaction, including the user's INSERT/UPDATE/DELETE. Better to fail the business operation than to let a `[Ledger]` table drop a row from its chain.

2. **No auto-repair of a broken chain.** `Verify<T>` returns a structured `VerificationResult` describing the failure; the framework never rewrites hashes, deletes corrupted rows, or "heals" the chain. Remediation is a deliberate, audited operator action.

3. **Invalid canonical bytes throw on read.** `LedgerEntity<T>.Deserialize()` and `VerifySelfHash()` throw on unreadable bytes or unknown `FormatVersion`. They never return a partial `T` or a null.

4. **Startup anomalies halt startup.** If a `[Ledger]` table's chain head fails self-hash at boot, or `_History` schema has diverged from the source entity un-migratably, startup throws. Continuing to write on top of a corrupt chain would compound the damage. Explicit operator action (CLI, config flag) is required to proceed.

5. **Only two cases warn-and-continue:** sticky-tracking attribute/table disagreement (§ Lifecycle) and schema-drift marker writes (§ Schema Drift). Both log WARN loudly. Nothing else gets this treatment.

### `ITrackingErrorHandler`

DI seam for developer control over write-path failures:

```csharp
public interface ITrackingErrorHandler
{
    // Called when a ledger/history write raises an exception other than the
    // automatically-retried UNIQUE(PrevHash) race. The handler decides disposition.
    TrackingErrorDisposition OnWriteFailure(TrackingWriteFailure failure);
}

public sealed class TrackingWriteFailure
{
    public string    TableName { get; init; } = "";
    public char      Operation { get; init; }   // I/U/D
    public object?   Entity    { get; init; }   // post-image, or pre-image for delete
    public Exception Exception { get; init; } = null!;
    public int       Attempt   { get; init; }   // 1-based, across retries
}

public enum TrackingErrorDisposition
{
    Rethrow,      // default — abort the source mutation
    DeadLetter,   // route failure to a developer-supplied sink; still rethrow
    Suppress,     // [Tracked] only — acknowledge the gap, do NOT rethrow;
                  // source mutation commits without a history row.
                  // Returning Suppress for a [Ledger] table raises
                  // LedgerSuppressNotAllowedException and rolls back.
}
```

Default implementation returns `Rethrow`. `Suppress` is named deliberately blunt: choosing it means you are knowingly accepting a gap in the audit trail. Use it only when the business cost of failing the user's operation genuinely exceeds the audit cost, and document the decision.

**`Suppress` always emits a framework WARN log** — `{TableName, Operation, PrimaryKey, timestamp, underlying exception}` — regardless of what the handler itself does. Silent suppression would contradict the "fail loud, never silently" rule even when suppression was the requested behavior: the gap needs to be visible to operators grepping logs, not just to whatever `DeadLetter` sink the handler may or may not wire up. The log is the minimum honest acknowledgment that the audit trail has a known hole at this point.

**`Suppress` is rejected on `[Ledger]` tables.** The integrity claim is all-or-nothing — there is no honest way to selectively drop a row from a hash-chained ledger. An auditor comparing anchored state against actual state cannot distinguish "suppressed gap" from "cascade-rewritten tamper", so silently tolerating the gap would contradict the "fail loud, never silently" rule. If you genuinely need an escape hatch on a currently-ledgered table, downgrade it to `[Tracked]` via `sp_ledger_drop` first — a deliberate, visible choice that voids prior integrity claims explicitly rather than silently.

### Handler scope — what to avoid

**The handler runs inside the live write transaction.** We strongly advise against:

- Calling `sp_tracking_drop` or `sp_ledger_drop` from the handler — these are DDL on the same tables the failed write touched; dropping them mid-transaction is undefined behavior on both SQLite and SQL Server.
- Re-provisioning tables or invoking AutoRepo migration — those are startup-time paths and will not behave correctly mid-request.
- Mutating schema, touching unrelated rows, or committing a separate transaction from inside the handler.

Destructive recovery (drop + re-provision, chain reset, history wipe) belongs at the **operator layer**, taken out-of-band with the existing ledger archived first if the data may later need to be defended. Treating it as an error-path reflex turns a cryptographic audit surface into best-effort storage.

## Exceptions

Framework-thrown exceptions specific to tracking / ledger operations. All derive from a base `TrackingException`:

| Exception | Thrown when |
|-----------|-------------|
| `LedgerRetryExhaustedException` | Unique-`PrevHash` retry loop exceeds the cap (~10). Indicates catastrophic contention; surfaces to the caller with the originating operation aborted. |
| `LedgerChainBrokenException` | `Verify<T>` detected a broken forward link or self-hash mismatch. Carries `FirstBadLedgerId` and `FailureKind` for programmatic recovery. |
| `LedgerChainHeadInvalidException` | Startup boot-time self-hash check on the current chain head failed. Writes refused for this table until an operator explicitly acknowledges (CLI command or config flag). |
| `LedgerCorruptBytesException` | `CanonicalBytes` on a ledger row cannot be deserialized (truncated, malformed, or incompatible with the declared `FormatVersion`). Raised by `Deserialize()` / `VerifySelfHash()` on read. |
| `LedgerFormatVersionMismatchException` | A row's `FormatVersion` is newer than the current build can parse. Bumped only across binary-incompatible `BinarySerializer` changes. |
| `TrackingWriteFailedException` | Wraps the underlying exception (DB error, serialization failure, etc.) when `ITrackingErrorHandler` returns `Rethrow` or `DeadLetter`. Carries `TableName`, `Operation`, and `Attempt`. |
| `LedgerSuppressNotAllowedException` | `ITrackingErrorHandler` returned `Suppress` for a `[Ledger]`-table failure. Source mutation is rolled back. Downgrade to `[Tracked]` via `sp_ledger_drop` if the escape hatch is genuinely needed. |
| `TrackingDowngradeRefusedException` | A caller attempted to drop tracking via code rather than `sp_tracking_drop(table, confirm)` / `sp_ledger_drop(table, confirm)`. |
| `TrackingSchemaMismatchException` | `_History` schema diverged from the source entity in a way AutoRepo cannot auto-migrate (e.g., type change on an existing column). Emitted at startup. |

## Admin Console Integration

SmartData.Console (see `docs/SmartData.Console.md`) surfaces tracking and ledger data through these tabs:

- **Entity → History:** renders `_History` rows for the selected entity row, with diff highlighting between consecutive versions. Backs onto `sp_entity_history`.
- **Entity → Ledger:** renders `_Ledger` rows for the selected entity, including synthetic prune markers and schema markers. Synthetic rows carry a distinct badge (`'P'` → "Pruned", `'S'` → "Schema change"). Schema markers expand to a two-column before/after diff of added vs. removed captured columns. Shows `RowHash` / `PrevHash` in short form with click-to-expand full bytes.
- **Ledger → Verify:** runs `sp_ledger_verify(table)` and displays the structured `VerificationResult`. Accepts an optional digest file for anchored verification.
- **Ledger → Digest:** runs `sp_ledger_digest(table)` and offers download as JSONL for pipelining into an external store.
- **Ledger → Schema History:** runs `sp_schema_history(table)` and renders the full schema-marker timeline (or the `SysTrackedColumns` row for `[Tracked]`-only tables). Each entry shows `DetectedAt`, `DetectedBy`, `CapturedHash`, and an expandable column list.

All tabs are read-only except Digest, which produces a downloadable artifact. The console does **not** expose `sp_tracking_drop` or `sp_ledger_prune` — those require explicit CLI invocation to prevent accidental clicks.

## Source map

Where the code lives, for readers jumping from this doc into the implementation:

| Concern | File / type |
|---|---|
| Attributes | `SmartData.Server/Attributes/{Tracked,Ledger,NotTracked}Attribute.cs` |
| Per-entity reflection snapshot | `TrackedEntityInfo<T>` |
| Generic row surfaces | `HistoryEntity<T>`, `LedgerEntity<T>`, `LedgerPayload<T>`, `SchemaMarker`, `CapturedColumn` |
| Fluent mapping registry | `TrackingMappingRegistry` |
| Schema provisioning | `TrackingSchemaManager<T>` (history + ledger + sidecar table) |
| Write-path orchestrator | `TrackingWritePath` (invoked from `DatabaseContext.Insert/Update/Delete/*Async`) |
| Ledger writer + retry loop | `LedgerWriter` (hash compute, genesis, drift-baseline, retry on `UNIQUE(PrevHash)`) |
| `[Tracked]`-only drift | `TrackedColumnSidecar` + `SysTrackedColumns` (`_sys_tracked_columns`) |
| Verification + digests | `LedgerVerifier` (both generic `Verify<T>` and non-generic `VerifyByTableName`) |
| Exceptions | `TrackingExceptions.cs`, `LedgerExceptions.cs` |
| Startup warning log | `TrackingLog` (wired by `UseSmartData`) |
| DI registration | `ServiceCollectionExtensions.AddSmartData` |
| System procedures | `SystemProcedures/Sp{EntityHistory,LedgerDigest,LedgerVerify,LedgerDrop,TrackingDrop,HistoryPrune,LedgerPrune,SchemaHistory}.cs` |
| Admin console | `SmartData.Console/Controllers/TrackingController.cs`, `Views/Tracking/*.cshtml` |
| Validation harness | `SmartApp.TrackingSpike/` — concurrency spike + smoke probes for verification / drift / prune / SQL Server parity |
