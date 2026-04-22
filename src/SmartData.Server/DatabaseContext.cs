using System.Linq.Expressions;
using LinqToDB;
using LinqToDB.Data;
using SmartData.Core.BinarySerialization;
using SmartData.Server.Attributes;
using SmartData.Server.Metrics;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.Tracking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SmartData.Server;

internal class DatabaseContext : IDatabaseContext, IDisposable
{
    private readonly IDatabaseProvider _provider;
    private readonly IServiceProvider _serviceProvider;
    private readonly BackgroundSpQueue _backgroundQueue;
    private readonly MetricsCollector? _metrics;
    private readonly MetricsOptions? _metricsOptions;
    private readonly SchemaMode _schemaMode;
    private readonly IndexOptions _indexOptions;
    private readonly TrackingWritePath _tracking;
    private readonly Dictionary<string, DataConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private string _activeDb = "";

    public IServiceProvider Services => _serviceProvider;

    public DatabaseContext(
        IDatabaseProvider provider,
        IServiceProvider serviceProvider,
        BackgroundSpQueue backgroundQueue,
        IOptions<SmartDataOptions> options,
        TrackingWritePath tracking,
        MetricsCollector? metrics = null)
    {
        _provider = provider;
        _serviceProvider = serviceProvider;
        _backgroundQueue = backgroundQueue;
        _metrics = metrics;
        _metricsOptions = options.Value.Metrics;
        _schemaMode = options.Value.SchemaMode;
        _indexOptions = options.Value.Index;
        _tracking = tracking;
    }

    public void UseDatabase(string dbName)
    {
        _activeDb = dbName;
    }

    // --- Sync data access ---

    public ITable<T> GetTable<T>() where T : class, new()
    {
        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);
        return GetOrCreateConnection(_activeDb).GetTable<T>();
    }

    public T Insert<T>(T entity) where T : class, new()
    {
        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);
        var conn = GetOrCreateConnection(_activeDb);

        void DoSourceInsert(T e)
        {
            if (IdentityProperty<T>.Exists)
                IdentityProperty<T>.Set(e, conn.InsertWithIdentity(e));
            else
                conn.Insert(e);
        }

        if (!_tracking.IsTracked<T>())
        {
            DoSourceInsert(entity);
            return entity;
        }
        return _tracking.TrackedInsert(conn, entity, DoSourceInsert);
    }

    public int Update<T>(T entity) where T : class, new()
    {
        var conn = GetOrCreateConnection(_activeDb);
        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);
        if (!_tracking.IsTracked<T>())
            return conn.Update(entity);
        return _tracking.TrackedUpdate(conn, entity, () => conn.Update(entity));
    }

    public int Delete<T>(T entity) where T : class, new()
    {
        var conn = GetOrCreateConnection(_activeDb);
        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);
        if (!_tracking.IsTracked<T>())
            return conn.Delete(entity);
        return _tracking.TrackedDelete(conn, entity, () => conn.Delete(entity));
    }

    public int Delete<T>(Expression<Func<T, bool>> predicate) where T : class, new()
    {
        var table = GetTable<T>();
        if (!_tracking.IsTracked<T>())
            return table.Where(predicate).Delete();
        var conn = GetOrCreateConnection(_activeDb);
        return _tracking.TrackedBulkDelete(conn, table, predicate, () => table.Where(predicate).Delete());
    }

    // --- History read path ---

    public IQueryable<HistoryEntity<T>> History<T>() where T : class, new()
    {
        if (TrackedEntityInfo<T>.DeclaredMode == TrackingMode.None)
            throw new InvalidOperationException(
                $"Entity '{typeof(T).Name}' is not tracked. Add [Tracked] to query history.");

        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);

        var conn = GetOrCreateConnection(_activeDb);
        _tracking.RegisterAndAttach<T>(conn);
        return conn.GetTable<HistoryEntity<T>>();
    }

    public IQueryable<LedgerEntity<T>> Ledger<T>() where T : class, new()
    {
        if (TrackedEntityInfo<T>.DeclaredMode != TrackingMode.Ledger)
            throw new InvalidOperationException(
                $"Entity '{typeof(T).Name}' is not ledgered. Add [Ledger] to query the ledger.");

        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);

        var conn = GetOrCreateConnection(_activeDb);
        _tracking.RegisterAndAttach<T>(conn);
        return conn.GetTable<LedgerEntity<T>>();
    }

    public VerificationResult Verify<T>() where T : class, new()
    {
        var conn = EnsureLedgerReady<T>();
        return LedgerVerifier.Verify<T>(conn);
    }

    public VerificationResult Verify<T>(IEnumerable<LedgerDigest> anchors) where T : class, new()
    {
        var conn = EnsureLedgerReady<T>();
        return LedgerVerifier.VerifyAnchored<T>(conn, anchors);
    }

    public LedgerDigest LedgerDigest<T>() where T : class, new()
    {
        var conn = EnsureLedgerReady<T>();
        return LedgerVerifier.ComputeDigest<T>(conn);
    }

    public VerificationResult Verify(string ledgerTableName)
        => LedgerVerifier.VerifyByTableName(GetOrCreateConnection(_activeDb), ledgerTableName);

    public VerificationResult Verify(string ledgerTableName, IEnumerable<LedgerDigest> anchors)
        => LedgerVerifier.VerifyAnchoredByTableName(GetOrCreateConnection(_activeDb), ledgerTableName, anchors);

    public LedgerDigest LedgerDigest(string ledgerTableName)
        => LedgerVerifier.ComputeDigestByTableName(GetOrCreateConnection(_activeDb), ledgerTableName);

    public IEnumerable<SchemaMarker> SchemaMarkers<T>() where T : class, new()
    {
        var conn = EnsureLedgerReady<T>();
        var synth = conn.GetTable<LedgerEntity<T>>()
            .Where(l => l.HistoryId == null)
            .OrderBy(l => l.LedgerId)
            .ToList();
        foreach (var row in synth)
        {
            SchemaMarker? marker = null;
            try
            {
                var payload = row.Deserialize();
                if (payload.Operation == "S") marker = payload.Synthetic?.Schema;
            }
            catch { /* skip unreadable synthetic row — surfaces via Verify */ }
            if (marker is not null) yield return marker;
        }
    }

    private DataConnection EnsureLedgerReady<T>() where T : class, new()
    {
        if (TrackedEntityInfo<T>.DeclaredMode != TrackingMode.Ledger)
            throw new InvalidOperationException(
                $"Entity '{typeof(T).Name}' is not ledgered. Add [Ledger] to use ledger operations.");

        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);

        var conn = GetOrCreateConnection(_activeDb);
        _tracking.RegisterAndAttach<T>(conn);
        return conn;
    }

    // --- Async data access ---

    public async Task<T> InsertAsync<T>(T entity, CancellationToken ct = default) where T : class, new()
    {
        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);
        var conn = GetOrCreateConnection(_activeDb);

        async Task DoSourceInsertAsync(T e, CancellationToken c)
        {
            if (IdentityProperty<T>.Exists)
                IdentityProperty<T>.Set(e, await conn.InsertWithIdentityAsync(e, token: c));
            else
                await conn.InsertAsync(e, token: c);
        }

        if (!_tracking.IsTracked<T>())
        {
            await DoSourceInsertAsync(entity, ct);
            return entity;
        }
        return await _tracking.TrackedInsertAsync(conn, entity, DoSourceInsertAsync, ct);
    }

    public Task<int> UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class, new()
    {
        var conn = GetOrCreateConnection(_activeDb);
        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);
        if (!_tracking.IsTracked<T>())
            return conn.UpdateAsync(entity, token: ct);
        return _tracking.TrackedUpdateAsync(conn, entity, c => conn.UpdateAsync(entity, token: c), ct);
    }

    public Task<int> DeleteAsync<T>(T entity, CancellationToken ct = default) where T : class, new()
    {
        var conn = GetOrCreateConnection(_activeDb);
        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);
        if (!_tracking.IsTracked<T>())
            return conn.DeleteAsync(entity, token: ct);
        return _tracking.TrackedDeleteAsync(conn, entity, c => conn.DeleteAsync(entity, token: c), ct);
    }

    public Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class, new()
    {
        var table = GetTable<T>();
        if (!_tracking.IsTracked<T>())
            return table.Where(predicate).DeleteAsync(ct);
        var conn = GetOrCreateConnection(_activeDb);
        return _tracking.TrackedBulkDeleteAsync(conn, table, predicate,
            c => table.Where(predicate).DeleteAsync(c), ct);
    }

    // --- Full-text search ---

    public List<T> FullTextSearch<T>(string searchTerm, int limit = 100) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return new List<T>();

        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);

        var ftsAttr = IndexMapping<T>.GetFullTextIndex();
        if (ftsAttr == null)
            throw new InvalidOperationException(
                $"Entity '{typeof(T).Name}' does not have a [FullTextIndex] attribute.");

        var tableName = EntityMapping<T>.GetTableName();
        var sql = _provider.BuildFullTextSearchSql(tableName, ftsAttr.Columns, limit);
        var conn = GetOrCreateConnection(_activeDb);
        return conn.Query<T>(sql, new { searchTerm }).ToList();
    }

    public async Task<List<T>> FullTextSearchAsync<T>(string searchTerm, int limit = 100, CancellationToken ct = default) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return new List<T>();

        if (_schemaMode == SchemaMode.Auto)
            SchemaManager<T>.EnsureSchema(_activeDb, _provider, _indexOptions);

        var ftsAttr = IndexMapping<T>.GetFullTextIndex();
        if (ftsAttr == null)
            throw new InvalidOperationException(
                $"Entity '{typeof(T).Name}' does not have a [FullTextIndex] attribute.");

        var tableName = EntityMapping<T>.GetTableName();
        var sql = _provider.BuildFullTextSearchSql(tableName, ftsAttr.Columns, limit);
        var conn = GetOrCreateConnection(_activeDb);
        return await conn.SetCommand(sql, new { searchTerm }).QueryToListAsync<T>(ct);
    }

    // --- Transactions ---

    public ITransaction BeginTransaction()
    {
        var conn = GetOrCreateConnection(_activeDb);
        return new DataConnectionTransaction(conn.BeginTransaction());
    }

    // --- Procedure execution ---

    private DataConnection GetOrCreateConnection(string dbName)
    {
        if (_connections.TryGetValue(dbName, out var conn))
            return conn;

        conn = _provider.OpenConnection(dbName);

        if (_metrics != null && _metrics.Enabled && _metricsOptions != null
            && !dbName.StartsWith(_metricsOptions.DatabasePrefix, StringComparison.OrdinalIgnoreCase))
        {
            conn.AddInterceptor(new SqlTrackingInterceptor(_metrics, dbName, _metricsOptions.SlowQueryThresholdMs));
        }

        _connections[dbName] = conn;
        return conn;
    }

    public async Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default)
    {
        var parameters = ObjectToDict(args);
        var executor = _serviceProvider.GetRequiredService<ProcedureExecutor>();
        var identity = _serviceProvider.GetRequiredService<RequestIdentity>();
        var result = await executor.ExecuteAsync(
            spName, parameters, ct,
            identity.Token, identity.Trusted, identity.TrustedUser);

        if (result is T typed)
            return typed;

        var bytes = BinarySerializer.Serialize(result);
        return BinarySerializer.Deserialize<T>(bytes)!;
    }

    public void QueueExecuteAsync(string spName, object? args = null)
    {
        var parameters = ObjectToDict(args);
        var identity = _serviceProvider.GetRequiredService<RequestIdentity>();
        _backgroundQueue.Enqueue(new BackgroundSpWork(
            spName, parameters,
            identity.Token, identity.Trusted, identity.TrustedUser));
    }

    private static Dictionary<string, object> ObjectToDict(object? args)
    {
        if (args == null) return new();
        if (args is Dictionary<string, object> dict) return dict;

        var bytes = BinarySerializer.Serialize(args);
        return BinarySerializer.Deserialize<Dictionary<string, object>>(bytes) ?? new();
    }

    public void Dispose()
    {
        foreach (var conn in _connections.Values)
            conn.Dispose();
        _connections.Clear();
    }
}

/// <summary>
/// Wraps LinqToDB's DataConnectionTransaction to implement ITransaction.
/// </summary>
internal class DataConnectionTransaction : ITransaction
{
    private readonly LinqToDB.Data.DataConnectionTransaction _tx;
    private bool _completed;

    public DataConnectionTransaction(LinqToDB.Data.DataConnectionTransaction tx)
    {
        _tx = tx;
    }

    public void Commit()
    {
        _tx.Commit();
        _completed = true;
    }

    public void Rollback()
    {
        _tx.Rollback();
        _completed = true;
    }

    public void Dispose()
    {
        if (!_completed)
            _tx.Rollback();
        _tx.Dispose();
    }
}
