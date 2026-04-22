using System.Linq.Expressions;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Logging;

namespace SmartData.Server.Tracking;

/// <summary>
/// Centralised history/ledger write-path logic. The single surface the
/// <c>DatabaseContext</c> calls into from its <c>Insert</c>/<c>Update</c>/
/// <c>Delete</c> wrappers.
///
/// <para>
/// Write ordering (phase 1 — <c>[Tracked]</c> only; ledger logic lands in
/// phase 2):
/// </para>
/// <list type="number">
///   <item>Source mutation runs in the caller's (or an auto-started) transaction.</item>
///   <item>A row is appended to <c>{Table}_History</c> with the post-image,
///         <c>Operation</c>, <c>ChangedOn</c>, and <c>ChangedBy</c>.</item>
///   <item>Failures consult <see cref="ITrackingErrorHandler"/>.</item>
/// </list>
/// </summary>
internal sealed class TrackingWritePath
{
    private readonly TrackingMappingRegistry _registry;
    private readonly ITrackingErrorHandler _errorHandler;
    private readonly ITrackingUserProvider _userProvider;
    private readonly LedgerWriter _ledgerWriter;
    private readonly TrackedColumnSidecar _sidecar;
    private readonly ILogger<TrackingWritePath>? _logger;

    public TrackingWritePath(
        TrackingMappingRegistry registry,
        ITrackingErrorHandler errorHandler,
        ITrackingUserProvider userProvider,
        TrackedColumnSidecar sidecar,
        ILogger<TrackingWritePath>? logger = null)
    {
        _registry = registry;
        _errorHandler = errorHandler;
        _userProvider = userProvider;
        _ledgerWriter = new LedgerWriter(userProvider);
        _sidecar = sidecar;
        _logger = logger;
    }

    public bool IsTracked<T>() where T : class, new()
        => TrackedEntityInfo<T>.DeclaredMode != TrackingMode.None;

    /// <summary>Ensures the tracking mapping schema is visible on this connection.</summary>
    public void AttachMapping(DataConnection conn)
    {
        // AddMappingSchema is a no-op if the same schema has already been combined in.
        conn.AddMappingSchema(_registry.Schema);
    }

    /// <summary>Registers <typeparamref name="T"/>'s history mapping and attaches the schema to <paramref name="conn"/>.</summary>
    public void RegisterAndAttach<T>(DataConnection conn) where T : class, new()
    {
        _registry.RegisterHistory<T>();
        AttachMapping(conn);
    }

    // ---- Synchronous entry points ----------------------------------------------------

    public T TrackedInsert<T>(DataConnection conn, T entity, Action<T> runSourceInsert) where T : class, new()
    {
        RunTracked<T>(conn, () =>
        {
            runSourceInsert(entity);
            WriteHistory(conn, entity, "I");
        });
        return entity;
    }

    public int TrackedUpdate<T>(DataConnection conn, T entity, Func<int> runSourceUpdate) where T : class, new()
    {
        int rows = 0;
        RunTracked<T>(conn, () =>
        {
            rows = runSourceUpdate();
            WriteHistory(conn, entity, "U");
        });
        return rows;
    }

    public int TrackedDelete<T>(DataConnection conn, T entity, Func<int> runSourceDelete) where T : class, new()
    {
        int rows = 0;
        RunTracked<T>(conn, () =>
        {
            // Pre-image is the entity the caller handed us; DELETE has no post-image, so
            // the last-known state is what we mirror.
            rows = runSourceDelete();
            WriteHistory(conn, entity, "D");
        });
        return rows;
    }

    public int TrackedBulkDelete<T>(DataConnection conn, ITable<T> table, Expression<Func<T, bool>> predicate,
        Func<int> runSourceBulkDelete) where T : class, new()
    {
        int rows = 0;
        RunTracked<T>(conn, () =>
        {
            // Materialize pre-images in PK order inside the transaction, then delete,
            // then append one history row per materialized entity. Cost is linear
            // in affected-row count (spec § Write Path → Bulk operations).
            var preImages = table.Where(predicate).ToList();
            rows = runSourceBulkDelete();
            foreach (var e in preImages)
                WriteHistory(conn, e, "D");
        });
        return rows;
    }

    // ---- Async entry points ----------------------------------------------------------

    public async Task<T> TrackedInsertAsync<T>(DataConnection conn, T entity,
        Func<T, CancellationToken, Task> runSourceInsert, CancellationToken ct) where T : class, new()
    {
        await RunTrackedAsync<T>(conn, async () =>
        {
            await runSourceInsert(entity, ct);
            WriteHistory(conn, entity, "I");
        });
        return entity;
    }

    public async Task<int> TrackedUpdateAsync<T>(DataConnection conn, T entity,
        Func<CancellationToken, Task<int>> runSourceUpdate, CancellationToken ct) where T : class, new()
    {
        int rows = 0;
        await RunTrackedAsync<T>(conn, async () =>
        {
            rows = await runSourceUpdate(ct);
            WriteHistory(conn, entity, "U");
        });
        return rows;
    }

    public async Task<int> TrackedDeleteAsync<T>(DataConnection conn, T entity,
        Func<CancellationToken, Task<int>> runSourceDelete, CancellationToken ct) where T : class, new()
    {
        int rows = 0;
        await RunTrackedAsync<T>(conn, async () =>
        {
            rows = await runSourceDelete(ct);
            WriteHistory(conn, entity, "D");
        });
        return rows;
    }

    public async Task<int> TrackedBulkDeleteAsync<T>(DataConnection conn, ITable<T> table,
        Expression<Func<T, bool>> predicate,
        Func<CancellationToken, Task<int>> runSourceBulkDelete, CancellationToken ct) where T : class, new()
    {
        int rows = 0;
        await RunTrackedAsync<T>(conn, async () =>
        {
            var preImages = await table.Where(predicate).ToListAsync(ct);
            rows = await runSourceBulkDelete(ct);
            foreach (var e in preImages)
                WriteHistory(conn, e, "D");
        });
        return rows;
    }

    // ---- Internals -------------------------------------------------------------------

    private void RunTracked<T>(DataConnection conn, Action body) where T : class, new()
    {
        _registry.RegisterHistory<T>();
        AttachMapping(conn);

        if (conn.Transaction is not null)
        {
            body();
            return;
        }

        using var tx = conn.BeginTransaction();
        body();
        tx.Commit();
    }

    private async Task RunTrackedAsync<T>(DataConnection conn, Func<Task> body) where T : class, new()
    {
        _registry.RegisterHistory<T>();
        AttachMapping(conn);

        if (conn.Transaction is not null)
        {
            await body();
            return;
        }

        await using var tx = await conn.BeginTransactionAsync();
        await body();
        await tx.CommitAsync();
    }

    private void WriteHistory<T>(DataConnection conn, T data, string operation) where T : class, new()
    {
        var now = DateTime.UtcNow;
        var user = _userProvider.CurrentUser;
        var history = new HistoryEntity<T>
        {
            Operation = operation,
            ChangedOn = now,
            ChangedBy = user,
            Data = data,
        };

        var historyTable = TrackedEntityInfo<T>.HistoryTableName;
        var mode = TrackedEntityInfo<T>.DeclaredMode;

        // [Tracked]-only drift detection — ledgered entities get their integrity-
        // protected schema markers via LedgerWriter instead.
        if (mode == TrackingMode.Tracked) _sidecar.CheckDrift<T>(conn, _userProvider);

        int attempt = 1;
        long historyId;
        try
        {
            historyId = Convert.ToInt64(conn.InsertWithIdentity(history));
            history.HistoryId = historyId;
        }
        catch (Exception ex)
        {
            HandleWriteFailure(data, operation, historyTable, attempt, ex, mode);
            return;
        }

        if (mode != TrackingMode.Ledger) return;

        // Paired ledger row — retry loop handles UNIQUE(PrevHash) races (§ Concurrency).
        try
        {
            _ledgerWriter.AppendEntityRow(conn, historyId, data, operation, now, user);
        }
        catch (LedgerRetryExhaustedException)
        {
            // Already a framework exception; let it propagate (aborts the txn).
            throw;
        }
        catch (Exception ex)
        {
            HandleWriteFailure(data, operation, TrackedEntityInfo<T>.LedgerTableName, attempt, ex, mode);
        }
    }

    private void HandleWriteFailure<T>(T data, string operation, string tableName, int attempt,
        Exception ex, TrackingMode mode) where T : class, new()
    {
        var disposition = _errorHandler.OnWriteFailure(new TrackingWriteFailure
        {
            TableName = tableName,
            Operation = operation,
            Entity = data,
            Exception = ex,
            Attempt = attempt,
        });

        switch (disposition)
        {
            case TrackingErrorDisposition.Suppress:
                if (mode == TrackingMode.Ledger)
                    throw new LedgerSuppressNotAllowedException(tableName);
                _logger?.LogWarning(ex,
                    "Tracking write SUPPRESSED on {Table} operation={Op} — audit gap accepted by handler.",
                    tableName, operation);
                return;

            case TrackingErrorDisposition.DeadLetter:
                _logger?.LogError(ex,
                    "Tracking write DEAD-LETTERED on {Table} operation={Op}; rethrowing.",
                    tableName, operation);
                throw new TrackingWriteFailedException(tableName, operation, attempt, ex);

            case TrackingErrorDisposition.Rethrow:
            default:
                throw new TrackingWriteFailedException(tableName, operation, attempt, ex);
        }
    }
}
