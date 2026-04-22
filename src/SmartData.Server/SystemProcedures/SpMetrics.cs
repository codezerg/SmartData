using System.Globalization;
using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Metrics;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using Microsoft.Extensions.Options;

namespace SmartData.Server.SystemProcedures;

internal class SpMetrics : SystemStoredProcedure<MetricsResult>
{
    private readonly MetricsCollector _collector;
    private readonly MetricsOptions _metricsOptions;

    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Source { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;

    public SpMetrics(MetricsCollector collector, IOptions<MetricsOptions> options)
    {
        _collector = collector;
        _metricsOptions = options.Value;
    }

    public override MetricsResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.ServerMetrics);

        var items = new List<MetricItem>();
        var source = Source?.ToLowerInvariant();

        // Live metrics (in-memory)
        if (source is null or "live")
        {
            var snapshot = _collector.GetSnapshot();
            items.AddRange(snapshot.Counters.Select(c => new MetricItem
            {
                Name = c.Name, Type = "counter", Tags = EmptyToNull(c.Tags),
                Value = c.Value, CreatedAt = DateTime.UtcNow
            }));
            items.AddRange(snapshot.Histograms.Select(h => new MetricItem
            {
                Name = h.Name, Type = "histogram", Tags = EmptyToNull(h.Tags),
                Value = h.Count > 0 ? h.Sum / h.Count : 0,
                Count = h.Count, Sum = h.Sum, Min = h.Min, Max = h.Max,
                P50 = h.P50, P95 = h.P95, P99 = h.P99, CreatedAt = DateTime.UtcNow
            }));
            items.AddRange(snapshot.Gauges.Select(g => new MetricItem
            {
                Name = g.Name, Type = "gauge", Tags = EmptyToNull(g.Tags),
                Value = g.Value, CreatedAt = DateTime.UtcNow
            }));
        }

        // Historical metrics (from rolling daily DBs)
        if (source is null or "db")
        {
            foreach (var dbName in GetMetricsDatabases(provider, From, To))
            {
                try
                {
                    SchemaManager<SysMetric>.EnsureSchema(dbName, provider);
                    using var conn = provider.OpenConnection(dbName);
                    var query = conn.GetTable<SysMetric>().AsQueryable();

                    if (Name != null)
                    {
                        if (Name.EndsWith("*"))
                        {
                            var prefix = Name.Substring(0, Name.Length - 1);
                            query = query.Where(m => m.Name.StartsWith(prefix));
                        }
                        else
                            query = query.Where(m => m.Name == Name);
                    }
                    if (Type != null)
                        query = query.Where(m => m.Type == Type);

                    items.AddRange(query.OrderByDescending(m => m.CreatedAt).Select(m => new MetricItem
                    {
                        Name = m.Name, Type = m.Type, Tags = m.Tags,
                        Value = m.Value, Count = m.Count, Sum = m.Sum,
                        Min = m.Min, Max = m.Max, P50 = m.P50, P95 = m.P95, P99 = m.P99,
                        CreatedAt = m.CreatedAt
                    }).ToList());
                }
                catch { /* DB may not exist yet or may be corrupted */ }
            }
        }

        // Apply name filter to live results too
        if (Name != null)
        {
            if (Name.EndsWith("*"))
            {
                var prefix = Name.Substring(0, Name.Length - 1);
                items = items.Where(i => i.Name.StartsWith(prefix)).ToList();
            }
            else
                items = items.Where(i => i.Name == Name).ToList();
        }
        if (Type != null)
            items = items.Where(i => i.Type == Type).ToList();

        var total = items.Count;
        var paged = items.Skip((Page - 1) * PageSize).Take(PageSize).ToList();

        return new MetricsResult { Items = paged, Total = total };
    }

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

            if (from.HasValue && date < from.Value.Date)
                continue;
            if (to.HasValue && date > to.Value.Date)
                continue;

            // Default: today only
            if (!from.HasValue && !to.HasValue && date != DateTime.UtcNow.Date)
                continue;

            result.Add(dbName);
        }

        return result;
    }

    private static string? EmptyToNull(TagSet tags) =>
        tags == TagSet.Empty ? null : tags.ToString();
}
