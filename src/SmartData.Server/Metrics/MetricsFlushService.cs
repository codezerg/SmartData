using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using LinqToDB;
using LinqToDB.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartData.Server.Entities;
using SmartData.Server.Providers;

namespace SmartData.Server.Metrics;

/// <summary>
/// Background service that periodically flushes in-memory metrics to rolling daily databases.
/// Uses IDatabaseProvider for all DB operations — fully provider-agnostic.
/// </summary>
internal sealed class MetricsFlushService : BackgroundService
{
    private readonly MetricsCollector _collector;
    private readonly IDatabaseProvider _provider;
    private readonly MetricsOptions _options;
    private readonly ILogger<MetricsFlushService> _logger;
    private readonly object _flushLock = new();
    private string _currentDbName = "";
    private DataConnection? _currentConnection;

    public MetricsFlushService(
        MetricsCollector collector,
        IDatabaseProvider provider,
        IOptions<MetricsOptions> options,
        ILogger<MetricsFlushService> logger)
    {
        _collector = collector;
        _provider = provider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
            return;

        // Subscribe to capacity threshold for early flush
        _collector.CapacityThresholdReached += OnCapacityThresholdReached;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.FlushIntervalSeconds), stoppingToken);
                Flush();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down — do a final flush
            Flush();
        }
        finally
        {
            _collector.CapacityThresholdReached -= OnCapacityThresholdReached;
            _currentConnection?.Dispose();
        }
    }

    private void OnCapacityThresholdReached()
    {
        // Fire-and-forget early flush — mutex prevents concurrent flushes
        Task.Run(() => Flush());
    }

    private void Flush()
    {
        if (!Monitor.TryEnter(_flushLock))
            return; // Another flush is in progress

        try
        {
            var sw = Stopwatch.StartNew();
            var conn = EnsureConnection();
            var data = _collector.CollectAndReset();

            FlushCounters(conn, data);
            FlushHistograms(conn, data);
            FlushGauges(conn, data);
            FlushSpans(conn, data);
            FlushExceptions(conn, data);
            RunRetention();

            sw.Stop();
            _logger.LogDebug("Metrics flush completed in {Duration}ms. Counters={Counters}, Histograms={Histograms}, Gauges={Gauges}, Spans={Spans}, Exceptions={Exceptions}",
                sw.ElapsedMilliseconds, data.Counters.Count, data.Histograms.Count, data.Gauges.Count, data.Spans.Count, data.Exceptions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metrics flush failed");
        }
        finally
        {
            Monitor.Exit(_flushLock);
        }
    }

    private DataConnection EnsureConnection()
    {
        var todayDbName = GetDatabaseName(DateTime.UtcNow);

        if (_currentDbName == todayDbName && _currentConnection != null)
            return _currentConnection;

        // Date rolled over or first run
        _currentConnection?.Dispose();

        if (!_provider.DatabaseExists(todayDbName))
            _provider.EnsureDatabase(todayDbName);

        // Ensure schema for all three tables
        SchemaManager<SysMetric>.EnsureSchema(todayDbName, _provider);
        SchemaManager<SysSpan>.EnsureSchema(todayDbName, _provider);
        SchemaManager<SysException>.EnsureSchema(todayDbName, _provider);

        // Open a bare connection — no SqlTrackingInterceptor
        _currentConnection = _provider.OpenConnection(todayDbName);
        _currentDbName = todayDbName;

        return _currentConnection;
    }

    private void FlushCounters(DataConnection conn, FlushData data)
    {
        var now = DateTime.UtcNow;
        foreach (var c in data.Counters)
        {
            conn.Insert(new SysMetric
            {
                Name = c.Name,
                Type = "counter",
                Tags = c.Tags == TagSet.Empty ? null : c.Tags.ToString(),
                Value = c.Value,
                CreatedAt = now
            });
        }
    }

    private void FlushHistograms(DataConnection conn, FlushData data)
    {
        var now = DateTime.UtcNow;
        foreach (var h in data.Histograms)
        {
            conn.Insert(new SysMetric
            {
                Name = h.Name,
                Type = "histogram",
                Tags = h.Tags == TagSet.Empty ? null : h.Tags.ToString(),
                Value = h.Count > 0 ? h.Sum / h.Count : 0, // average as Value
                Count = h.Count,
                Sum = h.Sum,
                Min = h.Min,
                Max = h.Max,
                P50 = h.P50,
                P95 = h.P95,
                P99 = h.P99,
                CreatedAt = now
            });
        }
    }

    private void FlushGauges(DataConnection conn, FlushData data)
    {
        var now = DateTime.UtcNow;
        foreach (var g in data.Gauges)
        {
            conn.Insert(new SysMetric
            {
                Name = g.Name,
                Type = "gauge",
                Tags = g.Tags == TagSet.Empty ? null : g.Tags.ToString(),
                Value = g.Value,
                CreatedAt = now
            });
        }
    }

    private void FlushSpans(DataConnection conn, FlushData data)
    {
        var now = DateTime.UtcNow;
        foreach (var s in data.Spans)
        {
            conn.Insert(new SysSpan
            {
                TraceId = s.TraceId,
                SpanId = s.SpanId,
                ParentSpanId = s.ParentSpanId,
                Name = s.Name,
                Tags = s.Tags == TagSet.Empty ? null : s.Tags.ToString(),
                Attributes = s.Attributes.Count > 0 ? JsonSerializer.Serialize(s.Attributes) : null,
                StartTime = s.StartTime,
                EndTime = s.EndTime ?? s.StartTime,
                DurationMs = s.Duration.TotalMilliseconds,
                Status = s.Status.ToString(),
                ErrorMessage = s.ErrorMessage,
                ErrorType = s.ErrorType,
                CreatedAt = now
            });
        }
    }

    private void FlushExceptions(DataConnection conn, FlushData data)
    {
        foreach (var e in data.Exceptions)
        {
            conn.Insert(new SysException
            {
                ExceptionType = e.ExceptionType,
                Message = e.Message,
                StackTrace = e.StackTrace,
                Procedure = e.Procedure,
                Database = e.Database,
                User = e.User,
                TraceId = e.TraceId,
                SpanId = e.SpanId,
                Properties = e.Properties.Count > 0 ? JsonSerializer.Serialize(e.Properties) : null,
                Timestamp = e.Timestamp
            });
        }
    }

    private void RunRetention()
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays);
        var prefix = _options.DatabasePrefix + "_";

        foreach (var dbName in _provider.ListDatabases())
        {
            if (!dbName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var datePart = dbName[prefix.Length..];
            if (DateTime.TryParseExact(datePart, "yyyy_MM_dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                if (date < cutoff)
                {
                    _logger.LogInformation("Deleting expired metrics database: {Database}", dbName);
                    _provider.DropDatabase(dbName);
                }
            }
        }
    }

    internal string GetDatabaseName(DateTime date)
    {
        return $"{_options.DatabasePrefix}_{date:yyyy_MM_dd}";
    }
}
