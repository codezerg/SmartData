using System.Globalization;
using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Metrics;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using Microsoft.Extensions.Options;

namespace SmartData.Server.SystemProcedures;

internal class SpTraces : SystemStoredProcedure<TracesResult>
{
    private readonly MetricsCollector _collector;
    private readonly MetricsOptions _metricsOptions;

    public string? TraceId { get; set; }
    public string? Procedure { get; set; }
    public string? Source { get; set; }
    public bool? ErrorsOnly { get; set; }
    public double? MinDurationMs { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public SpTraces(MetricsCollector collector, IOptions<MetricsOptions> options)
    {
        _collector = collector;
        _metricsOptions = options.Value;
    }

    public override TracesResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.ServerMetrics);

        var allSpans = new List<SpanItem>();
        var source = Source?.ToLowerInvariant();

        // Live spans (in-memory)
        if (source is null or "live")
        {
            var snapshot = _collector.GetSnapshot();
            allSpans.AddRange(snapshot.Spans.Select(SpanToItem));
        }

        // Historical spans (from rolling daily DBs)
        if (source is null or "db")
        {
            foreach (var dbName in GetMetricsDatabases(provider, From, To))
            {
                try
                {
                    SchemaManager<SysSpan>.EnsureSchema(dbName, provider);
                    using var conn = provider.OpenConnection(dbName);
                    var query = conn.GetTable<SysSpan>().AsQueryable();

                    if (TraceId != null)
                        query = query.Where(s => s.TraceId == TraceId);
                    if (Procedure != null)
                        query = query.Where(s => s.Tags != null && s.Tags.Contains(Procedure));

                    allSpans.AddRange(query.OrderByDescending(s => s.StartTime).Select(s => new SpanItem
                    {
                        TraceId = s.TraceId, SpanId = s.SpanId, ParentSpanId = s.ParentSpanId,
                        Name = s.Name, Tags = s.Tags, Attributes = s.Attributes,
                        StartTime = s.StartTime, EndTime = s.EndTime, DurationMs = s.DurationMs,
                        Status = s.Status, ErrorMessage = s.ErrorMessage, ErrorType = s.ErrorType
                    }).ToList());
                }
                catch { /* DB may not exist yet */ }
            }
        }

        // If TraceId specified, return full trace tree
        if (TraceId != null)
        {
            var traceSpans = allSpans.Where(s => s.TraceId == TraceId).ToList();
            return new TracesResult
            {
                Spans = traceSpans,
                Total = traceSpans.Count
            };
        }

        // Group spans into traces
        var traces = allSpans
            .GroupBy(s => s.TraceId)
            .Select(g =>
            {
                var root = g.FirstOrDefault(s => s.ParentSpanId == null) ?? g.First();
                return new TraceItem
                {
                    TraceId = g.Key,
                    RootSpanName = root.Name,
                    TotalDurationMs = root.DurationMs,
                    SpanCount = g.Count(),
                    HasErrors = g.Any(s => s.Status == "Error"),
                    StartTime = root.StartTime
                };
            })
            .ToList();

        // Apply filters
        if (ErrorsOnly == true)
            traces = traces.Where(t => t.HasErrors).ToList();
        if (MinDurationMs.HasValue)
            traces = traces.Where(t => t.TotalDurationMs >= MinDurationMs.Value).ToList();
        if (Procedure != null)
            traces = traces.Where(t => t.RootSpanName.Contains(Procedure, StringComparison.OrdinalIgnoreCase)).ToList();

        traces = traces.OrderByDescending(t => t.StartTime).ToList();
        var total = traces.Count;
        var paged = traces.Skip((Page - 1) * PageSize).Take(PageSize).ToList();

        return new TracesResult { Traces = paged, Total = total };
    }

    private static SpanItem SpanToItem(Span s) => new()
    {
        TraceId = s.TraceId,
        SpanId = s.SpanId,
        ParentSpanId = s.ParentSpanId,
        Name = s.Name,
        Tags = s.Tags == TagSet.Empty ? null : s.Tags.ToString(),
        Attributes = s.Attributes.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(s.Attributes) : null,
        StartTime = s.StartTime,
        EndTime = s.EndTime ?? s.StartTime,
        DurationMs = s.Duration.TotalMilliseconds,
        Status = s.Status.ToString(),
        ErrorMessage = s.ErrorMessage,
        ErrorType = s.ErrorType
    };

    private List<string> GetMetricsDatabases(IDatabaseProvider provider, DateTime? from, DateTime? to)
    {
        var prefix = _metricsOptions.DatabasePrefix + "_";
        var result = new List<string>();

        foreach (var dbName in provider.ListDatabases())
        {
            if (!dbName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var datePart = dbName[prefix.Length..];
            if (!DateTime.TryParseExact(datePart, "yyyy_MM_dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            if (from.HasValue && date < from.Value.Date) continue;
            if (to.HasValue && date > to.Value.Date) continue;
            if (!from.HasValue && !to.HasValue && date != DateTime.UtcNow.Date) continue;

            result.Add(dbName);
        }

        return result;
    }
}
