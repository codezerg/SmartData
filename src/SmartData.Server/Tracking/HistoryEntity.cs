namespace SmartData.Server.Tracking;

/// <summary>
/// Generic history-row surface for any tracked entity <typeparamref name="T"/>.
/// Mapped at startup via <c>FluentMappingBuilder</c> so that <c>h.Data.X</c>
/// flattens to a plain column read against <c>{Table}_History</c>.
///
/// <para>
/// Validated by <c>SmartApp.TrackingSpike</c>: <c>Where</c>/<c>OrderBy</c>/
/// <c>GroupBy</c>/joins/projections all produce clean flat SQL — see
/// <c>docs/SmartData.Server.Tracking.md</c> § Generic Entity Shape.
/// </para>
/// </summary>
public sealed class HistoryEntity<T> where T : class, new()
{
    /// <summary>Sequence within this history table (identity).</summary>
    public long HistoryId { get; set; }

    /// <summary><c>"I"</c>, <c>"U"</c>, or <c>"D"</c> (single-character string — <c>char</c> isn't in the <c>BinarySerializer</c>'s primitive table).</summary>
    public string Operation { get; set; } = "";

    /// <summary>UTC timestamp of the mutation.</summary>
    public DateTime ChangedOn { get; set; }

    /// <summary>Resolved from the ambient user provider at write time.</summary>
    public string ChangedBy { get; set; } = "";

    /// <summary>
    /// Post-image of the source entity for INSERT/UPDATE; last-known state for
    /// DELETE. Hydrated from flattened columns on read.
    /// </summary>
    public T Data { get; set; } = new();
}
