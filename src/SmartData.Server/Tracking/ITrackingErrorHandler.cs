namespace SmartData.Server.Tracking;

/// <summary>
/// DI seam that lets applications decide how to respond to tracking/ledger
/// write failures. The default implementation returns
/// <see cref="TrackingErrorDisposition.Rethrow"/>, which aborts the source
/// mutation and preserves the "fail loud, never silently" rule.
/// </summary>
public interface ITrackingErrorHandler
{
    /// <summary>
    /// Called when a history/ledger write raises an exception other than the
    /// automatically-retried <c>UNIQUE(PrevHash)</c> race. The handler decides
    /// disposition. Returning <see cref="TrackingErrorDisposition.Suppress"/>
    /// on a <c>[Ledger]</c> table raises <c>LedgerSuppressNotAllowedException</c>.
    /// </summary>
    TrackingErrorDisposition OnWriteFailure(TrackingWriteFailure failure);
}

/// <summary>
/// Context handed to <see cref="ITrackingErrorHandler.OnWriteFailure"/>.
/// </summary>
public sealed class TrackingWriteFailure
{
    public string TableName { get; init; } = "";
    public string Operation { get; init; } = "";
    public object? Entity { get; init; }
    public Exception Exception { get; init; } = null!;
    public int Attempt { get; init; }
}

/// <summary>
/// Outcome the handler asks the framework to take.
/// </summary>
public enum TrackingErrorDisposition
{
    /// <summary>Default — abort the source mutation.</summary>
    Rethrow,

    /// <summary>
    /// Route the failure to a developer-supplied sink (log, queue, incident
    /// system); still rethrow afterwards.
    /// </summary>
    DeadLetter,

    /// <summary>
    /// <b><c>[Tracked]</c> only.</b> Commit the source mutation without a
    /// history row. A WARN-level log entry is always emitted regardless of
    /// handler choice, so the gap is visible to operators.
    ///
    /// <para>
    /// Returning <see cref="Suppress"/> for a <c>[Ledger]</c> table raises
    /// <c>LedgerSuppressNotAllowedException</c> and rolls back.
    /// </para>
    /// </summary>
    Suppress,
}

/// <summary>
/// Default implementation — always rethrow. Registered unless the application
/// supplies its own <see cref="ITrackingErrorHandler"/>.
/// </summary>
internal sealed class DefaultTrackingErrorHandler : ITrackingErrorHandler
{
    public TrackingErrorDisposition OnWriteFailure(TrackingWriteFailure failure) => TrackingErrorDisposition.Rethrow;
}
