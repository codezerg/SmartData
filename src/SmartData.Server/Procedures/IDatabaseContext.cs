using System.Linq.Expressions;
using LinqToDB;
using SmartData.Server.Tracking;

namespace SmartData.Server.Procedures;

public interface IDatabaseContext
{
    // Data access (sync)
    ITable<T> GetTable<T>() where T : class, new();
    T Insert<T>(T entity) where T : class, new();
    int Update<T>(T entity) where T : class, new();
    int Delete<T>(T entity) where T : class, new();
    int Delete<T>(Expression<Func<T, bool>> predicate) where T : class, new();

    // History (read) — for tracked entities. See docs/SmartData.Server.Tracking.md.
    IQueryable<HistoryEntity<T>> History<T>() where T : class, new();

    // Ledger (read) — for [Ledger]-annotated entities.
    IQueryable<LedgerEntity<T>> Ledger<T>() where T : class, new();

    /// <summary>Internal chain consistency check — forward-walks every row.</summary>
    VerificationResult Verify<T>() where T : class, new();

    /// <summary>Anchored verification — internal walk plus hash matching against each supplied <see cref="LedgerDigest"/>.</summary>
    VerificationResult Verify<T>(IEnumerable<LedgerDigest> anchors) where T : class, new();

    /// <summary>Capture the current chain-head digest for external anchoring.</summary>
    LedgerDigest LedgerDigest<T>() where T : class, new();

    /// <summary>Non-generic verify — used by <c>sp_ledger_verify</c> which dispatches on a runtime table name.</summary>
    VerificationResult Verify(string ledgerTableName);

    /// <summary>Non-generic verify with anchors.</summary>
    VerificationResult Verify(string ledgerTableName, IEnumerable<LedgerDigest> anchors);

    /// <summary>Non-generic digest.</summary>
    LedgerDigest LedgerDigest(string ledgerTableName);

    /// <summary>
    /// Schema-marker timeline for a ledgered entity. Walks synthetic rows
    /// (<c>HistoryId IS NULL</c>) and yields decoded <see cref="SchemaMarker"/>s
    /// in <c>LedgerId</c> order. Throws for <c>[Tracked]</c>-only entities —
    /// query <c>SysTrackedColumns</c> directly.
    /// </summary>
    IEnumerable<SchemaMarker> SchemaMarkers<T>() where T : class, new();

    // Data access (async)
    Task<T> InsertAsync<T>(T entity, CancellationToken ct = default) where T : class, new();
    Task<int> UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class, new();
    Task<int> DeleteAsync<T>(T entity, CancellationToken ct = default) where T : class, new();
    Task<int> DeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class, new();

    // Full-text search
    List<T> FullTextSearch<T>(string searchTerm, int limit = 100) where T : class, new();
    Task<List<T>> FullTextSearchAsync<T>(string searchTerm, int limit = 100, CancellationToken ct = default) where T : class, new();

    // Transactions
    ITransaction BeginTransaction();

    // Procedure execution
    Task<T> ExecuteAsync<T>(string spName, object? args = null, CancellationToken ct = default);
    void QueueExecuteAsync(string spName, object? args = null);

    // Context
    void UseDatabase(string dbName);
    IServiceProvider Services { get; }
}
