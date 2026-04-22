using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using LinqToDB.Interceptors;

namespace SmartData.Server.Metrics;

/// <summary>
/// linq2db CommandInterceptor that automatically creates child spans for every SQL command.
/// Captures SQL text, duration, rows affected, and feeds aggregate metrics.
/// Wired on application DB connections only — never on metrics DB connections.
/// </summary>
internal sealed class SqlTrackingInterceptor : CommandInterceptor
{
    private static readonly Regex _tableRegex = new(
        @"\b(?:FROM|INTO|UPDATE|DELETE\s+FROM)\s+[`\[\""']?(\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _opRegex = new(
        @"^\s*(SELECT|INSERT|UPDATE|DELETE)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly MetricsCollector _metrics;
    private readonly string _dbName;
    private readonly int _slowQueryThresholdMs;

    // AsyncLocal to pass context from CommandInitialized to AfterExecute
    private static readonly AsyncLocal<CommandContext?> _currentCommand = new();

    public SqlTrackingInterceptor(MetricsCollector metrics, string dbName, int slowQueryThresholdMs)
    {
        _metrics = metrics;
        _dbName = dbName;
        _slowQueryThresholdMs = slowQueryThresholdMs;
    }

    public override DbCommand CommandInitialized(CommandEventData eventData, DbCommand command)
    {
        if (!_metrics.Enabled)
            return command;

        var sql = command.CommandText;
        var op = ParseOp(sql);
        var table = ParseTable(sql);
        var procedure = GetCurrentProcedure();

        var metricTags = procedure != null
            ? new TagSet(("db", _dbName), ("op", op), ("table", table), ("procedure", procedure))
            : new TagSet(("db", _dbName), ("op", op), ("table", table));

        var span = _metrics.StartSpan("db.sql",
            ("db", _dbName), ("op", op), ("table", table));

        span.SetAttribute("sql", sql);
        if (procedure != null)
            span.SetAttribute("procedure", procedure);

        _currentCommand.Value = new CommandContext(span, Stopwatch.StartNew(), metricTags);
        return command;
    }

    private static string? GetCurrentProcedure() => MetricsContext.CurrentProcedure;

    public override void AfterExecuteReader(
        CommandEventData eventData, DbCommand command,
        CommandBehavior commandBehavior, DbDataReader ret)
    {
        FinishCommand(ret.RecordsAffected);
    }

    private void FinishCommand(int rowsAffected)
    {
        var ctx = _currentCommand.Value;
        if (ctx == null) return;

        ctx.Stopwatch.Stop();
        _currentCommand.Value = null;

        var durationMs = ctx.Stopwatch.Elapsed.TotalMilliseconds;

        ctx.Span.SetAttribute("duration_ms", durationMs);
        if (rowsAffected >= 0)
            ctx.Span.SetAttribute("rows", rowsAffected);

        // Feed aggregate metrics
        _metrics.Counter("sql.queries").Add(1, ctx.MetricTags);
        _metrics.Histogram("sql.duration_ms").Record(durationMs, ctx.MetricTags);
        if (rowsAffected > 0)
            _metrics.Counter("sql.rows").Add(rowsAffected, ctx.MetricTags);

        if (durationMs > _slowQueryThresholdMs)
            ctx.Span.SetAttribute("slow", "true");

        ctx.Span.Dispose();
    }

    private static string ParseOp(string sql)
    {
        try
        {
            var match = _opRegex.Match(sql);
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string ParseTable(string sql)
    {
        try
        {
            var match = _tableRegex.Match(sql);
            return match.Success ? match.Groups[1].Value : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private sealed record CommandContext(ISpan Span, Stopwatch Stopwatch, TagSet MetricTags);
}
