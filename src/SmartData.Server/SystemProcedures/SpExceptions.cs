using System.Globalization;
using LinqToDB;
using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Metrics;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using Microsoft.Extensions.Options;

namespace SmartData.Server.SystemProcedures;

internal class SpExceptions : SystemStoredProcedure<ExceptionsResult>
{
    private readonly MetricsCollector _collector;
    private readonly MetricsOptions _metricsOptions;

    public string? ExceptionType { get; set; }
    public string? Procedure { get; set; }
    public string? Source { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;

    public SpExceptions(MetricsCollector collector, IOptions<MetricsOptions> options)
    {
        _collector = collector;
        _metricsOptions = options.Value;
    }

    public override ExceptionsResult Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.ServerMetrics);

        var items = new List<ExceptionItem>();
        var source = Source?.ToLowerInvariant();

        // Live exceptions (in-memory)
        if (source is null or "live")
        {
            var snapshot = _collector.GetSnapshot();
            items.AddRange(snapshot.Exceptions.Select(e => new ExceptionItem
            {
                ExceptionType = e.ExceptionType,
                Message = e.Message,
                StackTrace = e.StackTrace,
                Procedure = e.Procedure,
                Database = e.Database,
                User = e.User,
                TraceId = e.TraceId,
                SpanId = e.SpanId,
                Properties = e.Properties.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(e.Properties) : null,
                Timestamp = e.Timestamp
            }));
        }

        // Historical exceptions (from rolling daily DBs)
        if (source is null or "db")
        {
            foreach (var dbName in GetMetricsDatabases(provider, From, To))
            {
                try
                {
                    SchemaManager<SysException>.EnsureSchema(dbName, provider);
                    using var conn = provider.OpenConnection(dbName);
                    var query = conn.GetTable<SysException>().AsQueryable();

                    if (ExceptionType != null)
                        query = query.Where(e => e.ExceptionType.Contains(ExceptionType));
                    if (Procedure != null)
                        query = query.Where(e => e.Procedure != null && e.Procedure == Procedure);

                    items.AddRange(query.OrderByDescending(e => e.Timestamp).Select(e => new ExceptionItem
                    {
                        ExceptionType = e.ExceptionType,
                        Message = e.Message,
                        StackTrace = e.StackTrace,
                        Procedure = e.Procedure,
                        Database = e.Database,
                        User = e.User,
                        TraceId = e.TraceId,
                        SpanId = e.SpanId,
                        Properties = e.Properties,
                        Timestamp = e.Timestamp
                    }).ToList());
                }
                catch { /* DB may not exist yet */ }
            }
        }

        // Apply filters to combined results
        if (ExceptionType != null)
            items = items.Where(i => i.ExceptionType.Contains(ExceptionType, StringComparison.OrdinalIgnoreCase)).ToList();
        if (Procedure != null)
            items = items.Where(i => i.Procedure == Procedure).ToList();

        items = items.OrderByDescending(i => i.Timestamp).ToList();
        var total = items.Count;
        var paged = items.Skip((Page - 1) * PageSize).Take(PageSize).ToList();

        return new ExceptionsResult { Items = paged, Total = total };
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

            if (from.HasValue && date < from.Value.Date) continue;
            if (to.HasValue && date > to.Value.Date) continue;
            if (!from.HasValue && !to.HasValue && date != DateTime.UtcNow.Date) continue;

            result.Add(dbName);
        }

        return result;
    }
}
