namespace SmartData.Server.Tracking;

/// <summary>Base type for all tracking/ledger framework exceptions.</summary>
public abstract class TrackingException : SmartDataException
{
    protected TrackingException(string message) : base(message) { }
    protected TrackingException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Wraps an underlying exception (DB error, serialization failure, etc.) when
/// <see cref="ITrackingErrorHandler"/> returns <see cref="TrackingErrorDisposition.Rethrow"/>
/// or <see cref="TrackingErrorDisposition.DeadLetter"/>. Carries table/operation
/// context for diagnostics.
/// </summary>
public sealed class TrackingWriteFailedException : TrackingException
{
    public string TableName { get; }
    public string Operation { get; }
    public int Attempt { get; }

    public TrackingWriteFailedException(string tableName, string operation, int attempt, Exception inner)
        : base($"Tracking write failed on '{tableName}' ({operation}, attempt {attempt}): {inner.Message}", inner)
    {
        TableName = tableName;
        Operation = operation;
        Attempt = attempt;
    }
}

/// <summary>
/// Raised at startup when <c>{Table}_History</c>'s schema has diverged from the
/// source entity in a way AutoRepo cannot auto-migrate (type change on an
/// existing column). Writer refuses to proceed — operator action required.
/// </summary>
public sealed class TrackingSchemaMismatchException : TrackingException
{
    public string TableName { get; }

    public TrackingSchemaMismatchException(string tableName, string detail)
        : base($"History schema mismatch on '{tableName}': {detail}")
    {
        TableName = tableName;
    }
}

/// <summary>
/// Raised when a caller attempts to drop tracking via code rather than
/// <c>sp_tracking_drop(table, confirm)</c> / <c>sp_ledger_drop(table, confirm)</c>.
/// Tracking is sticky; removal is a deliberate, audited operator action.
/// </summary>
public sealed class TrackingDowngradeRefusedException : TrackingException
{
    public string TableName { get; }

    public TrackingDowngradeRefusedException(string tableName)
        : base($"Tracking cannot be disabled on '{tableName}' by attribute removal. " +
               "Use sp_tracking_drop(table, confirm) to explicitly drop, or sp_ledger_drop for ledger downgrade.")
    {
        TableName = tableName;
    }
}

/// <summary>
/// Raised when a caller attempts a LINQ-bulk update (<c>table.Where(…).Update(…)</c>)
/// against a tracked or ledgered table. Bulk updates are not intercepted in v1;
/// rewrite as a materialize-and-iterate helper, or downgrade the table.
/// </summary>
public sealed class TrackingBulkUpdateNotSupportedException : TrackingException
{
    public string TableName { get; }

    public TrackingBulkUpdateNotSupportedException(string tableName)
        : base($"Bulk LINQ updates on tracked table '{tableName}' are not supported in this version. " +
               "Materialize the affected rows and update each through ctx.Update(entity).")
    {
        TableName = tableName;
    }
}
