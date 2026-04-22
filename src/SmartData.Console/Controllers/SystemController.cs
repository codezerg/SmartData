using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Contracts;
using SmartData.Server;
using SmartData.Server.Backup;

namespace SmartData.Console.Controllers;

public class SystemController : ConsoleBaseController
{
    private readonly BackupService _backupService;
    private readonly ProcedureCatalog _catalog;

    public SystemController(IAuthenticatedProcedureService procedureService, BackupService backupService, ProcedureCatalog catalog) : base(procedureService)
    {
        _backupService = backupService;
        _catalog = catalog;
    }

    [HttpGet("/console/logs")]
    public async Task<IActionResult> Logs(string? type, string? procedure, string? search, CancellationToken ct)
    {
        var logs = await ExecuteAsync<List<LogEntry>>("sp_logs", new { Limit = 500 }, ct);

        if (!string.IsNullOrEmpty(type))
            logs = logs.Where(l => l.Type.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrEmpty(procedure))
            logs = logs.Where(l => l.ProcedureName.Contains(procedure, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrEmpty(search))
            logs = logs.Where(l => l.Message.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

        var model = new LogsViewModel
        {
            Logs = logs,
            FilterType = type,
            FilterProcedure = procedure,
            Search = search
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Logs", model);
    }

    [HttpGet("/console/storage")]
    public async Task<IActionResult> Storage(CancellationToken ct)
    {
        var storage = await ExecuteAsync<StorageResult>("sp_storage", ct: ct);
        var model = new StorageViewModel { Storage = storage };
        await PopulateLayout(null, ct);
        return PageOrPartial("Storage", model);
    }

    [HttpGet("/console/backups")]
    public async Task<IActionResult> Backups(CancellationToken ct)
    {
        var backups = await ExecuteAsync<List<BackupListItem>>("sp_backup_list", ct: ct);
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        var model = new BackupsViewModel
        {
            Backups = backups,
            Databases = databases,
            ActiveTab = "backups"
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Backups", model);
    }

    [HttpGet("/console/backups/history")]
    public async Task<IActionResult> BackupHistory(CancellationToken ct)
    {
        var backups = await ExecuteAsync<List<BackupListItem>>("sp_backup_list", ct: ct);
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        var history = _backupService.GetHistory();
        var model = new BackupsViewModel
        {
            Backups = backups,
            Databases = databases,
            History = history,
            ActiveTab = "history"
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Backups", model);
    }

    [HttpPost("/console/backups/create")]
    public async Task<IActionResult> CreateBackup([FromForm] string[] databases, CancellationToken ct)
    {
        try
        {
            var dbList = databases.Length == 0 ? "*" : string.Join(",", databases);
            var result = await ExecuteAsync<BackupCreateResult>("sp_backup_create", new { Databases = dbList }, ct);
            return await BackupsPageWithJob(result.JobId, ct);
        }
        catch (Exception ex)
        {
            return await BackupsPageWithError(ex.Message, ct);
        }
    }

    [HttpDelete("/console/backups/{id}")]
    public async Task<IActionResult> DeleteBackup(string id, CancellationToken ct)
    {
        string? successMessage = null;
        string? errorMessage = null;

        try
        {
            await ExecuteAsync<string>("sp_backup_drop", new { BackupId = id }, ct);
            successMessage = $"Backup {id} deleted.";
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        var backups = await ExecuteAsync<List<BackupListItem>>("sp_backup_list", ct: ct);
        var model = new BackupsViewModel
        {
            Backups = backups,
            SuccessMessage = successMessage,
            ErrorMessage = errorMessage
        };
        return PartialView("_BackupsTable", model);
    }

    [HttpPost("/console/backups/{id}/restore")]
    public async Task<IActionResult> RestoreBackup(string id, [FromForm] bool force, CancellationToken ct)
    {
        try
        {
            var result = await ExecuteAsync<BackupRestoreResult>("sp_backup_restore", new { BackupId = id, Force = force }, ct);
            return await BackupsTableWithJob(result.JobId, ct);
        }
        catch (Exception ex)
        {
            var backups = await ExecuteAsync<List<BackupListItem>>("sp_backup_list", ct: ct);
            var model = new BackupsViewModel { Backups = backups, ErrorMessage = ex.Message };
            return PartialView("_BackupsTable", model);
        }
    }

    [HttpGet("/console/backups/jobs/{jobId}")]
    public async Task<IActionResult> JobStatus(string jobId, CancellationToken ct)
    {
        try
        {
            var status = await ExecuteAsync<BackupJobStatus>("sp_backup_status", new { JobId = jobId }, ct);
            return PartialView("_BackupJobProgress", status);
        }
        catch
        {
            return PartialView("_BackupJobProgress", new BackupJobStatus { Status = "completed", JobId = jobId });
        }
    }

    [HttpPost("/console/backups/jobs/{jobId}/cancel")]
    public async Task<IActionResult> CancelJob(string jobId, CancellationToken ct)
    {
        try
        {
            await ExecuteAsync<string>("sp_backup_cancel", new { JobId = jobId }, ct);
        }
        catch { }

        return PartialView("_BackupJobProgress", new BackupJobStatus { Status = "cancelled", JobId = jobId });
    }

    [HttpGet("/console/backups/{id}/download")]
    public IActionResult DownloadBackup(string id)
    {
        var stream = _backupService.OpenDownloadStream(id);
        return File(stream, "application/octet-stream", $"{id}.smartbackup");
    }

    [HttpPost("/console/backups/upload")]
    public async Task<IActionResult> UploadChunk(CancellationToken ct)
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync(ct);
        var payload = System.Text.Json.JsonSerializer.Deserialize<UploadChunkPayload>(json,
            new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

        if (payload == null)
            return BadRequest(new { error = "Invalid payload" });

        try
        {
            var data = Convert.FromBase64String(payload.Data);
            var result = await ExecuteAsync<BackupUploadResult>("sp_backup_upload", new
            {
                BackupId = payload.BackupId ?? "",
                Data = data,
                Offset = payload.Offset,
                TotalSize = payload.TotalSize
            }, ct);

            return Json(new
            {
                backupId = result.BackupId,
                offset = result.Offset,
                totalSize = result.TotalSize,
                done = result.Done
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("/console/procedures")]
    public async Task<IActionResult> Procedures(string? tab, CancellationToken ct)
    {
        var all = _catalog.GetAll();
        var procedures = all.Select(kvp =>
        {
            var name = kvp.Key;
            var type = kvp.Value;
            var category = name.StartsWith("sp_") ? "system" : "user";

            var parameters = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(p => p.CanWrite && p.GetSetMethod() != null)
                .Select(p => new ProcedureParameter
                {
                    Name = p.Name,
                    Type = FormatTypeName(p.PropertyType),
                    IsOptional = Nullable.GetUnderlyingType(p.PropertyType) != null || !p.PropertyType.IsValueType
                })
                .ToList();

            var returnType = "";
            var baseType = type.BaseType;
            if (baseType is { IsGenericType: true })
            {
                var genericArg = baseType.GetGenericArguments().FirstOrDefault();
                if (genericArg != null)
                    returnType = FormatTypeName(genericArg);
            }

            return new ProcedureInfo
            {
                Name = name,
                Category = category,
                Parameters = parameters,
                ReturnType = returnType
            };
        })
        .OrderBy(p => p.Category == "system" ? 0 : 1)
        .ThenBy(p => p.Name)
        .ToList();

        var activeTab = tab ?? "all";
        var filtered = activeTab switch
        {
            "system" => procedures.Where(p => p.Category == "system").ToList(),
            "user" => procedures.Where(p => p.Category == "user").ToList(),
            _ => procedures
        };

        var model = new ProceduresViewModel
        {
            Procedures = filtered,
            SystemCount = procedures.Count(p => p.Category == "system"),
            UserCount = procedures.Count(p => p.Category == "user"),
            ActiveTab = activeTab
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Procedures", model);
    }

    private static string FormatTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null)
            return FormatTypeName(underlying) + "?";

        if (type.IsGenericType)
        {
            var name = type.Name[..type.Name.IndexOf('`')];
            var args = string.Join(", ", type.GetGenericArguments().Select(FormatTypeName));
            return $"{name}<{args}>";
        }

        return type.Name switch
        {
            "String" => "string",
            "Int32" => "int",
            "Int64" => "long",
            "Boolean" => "bool",
            "Double" => "double",
            "Single" => "float",
            "Decimal" => "decimal",
            "DateTime" => "DateTime",
            "Byte[]" => "byte[]",
            _ => type.Name
        };
    }

    // --- Metrics ---

    [HttpGet("/console/metrics")]
    public async Task<IActionResult> Metrics(string? tab, string? name, string? source, CancellationToken ct)
    {
        var args = new Dictionary<string, object?> { ["Source"] = source ?? "live", ["PageSize"] = 500 };
        if (!string.IsNullOrEmpty(name))
            args["Name"] = name;

        var metrics = await ExecuteAsync<MetricsResult>("sp_metrics", args, ct);

        var model = new MetricsViewModel
        {
            Metrics = metrics,
            ActiveTab = tab ?? "overview",
            FilterName = name,
            FilterSource = source
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Metrics", model);
    }

    // --- Exceptions ---

    [HttpGet("/console/exceptions")]
    public async Task<IActionResult> Exceptions(string? type, string? procedure, CancellationToken ct)
    {
        var args = new Dictionary<string, object?> { ["PageSize"] = 100 };
        if (!string.IsNullOrEmpty(type))
            args["Type"] = type;
        if (!string.IsNullOrEmpty(procedure))
            args["Procedure"] = procedure;

        var exceptions = await ExecuteAsync<ExceptionsResult>("sp_exceptions", args, ct);

        var model = new ExceptionsViewModel
        {
            Exceptions = exceptions,
            FilterType = type,
            FilterProcedure = procedure
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Exceptions", model);
    }

    // --- Traces ---

    [HttpGet("/console/traces")]
    public async Task<IActionResult> Traces(string? tab, string? procedure, bool? errorsOnly, double? minDuration, CancellationToken ct)
    {
        var args = new Dictionary<string, object?> { ["PageSize"] = 200 };
        if (!string.IsNullOrEmpty(procedure))
            args["Procedure"] = procedure;
        if (errorsOnly == true)
            args["ErrorsOnly"] = true;
        if (minDuration.HasValue)
            args["MinDurationMs"] = minDuration.Value;

        var traces = await ExecuteAsync<TracesResult>("sp_traces", args, ct);

        // Build per-procedure stats from trace list
        var procedureStats = traces.Traces
            .GroupBy(t => t.RootSpanName)
            .Select(g =>
            {
                var sorted = g.OrderBy(t => t.TotalDurationMs).ToList();
                var p95Index = Math.Max(0, (int)Math.Ceiling(sorted.Count * 0.95) - 1);
                return new ProcedureTraceStats
                {
                    Procedure = g.Key,
                    CallCount = g.Count(),
                    ErrorCount = g.Count(t => t.HasErrors),
                    AvgDurationMs = g.Average(t => t.TotalDurationMs),
                    MaxDurationMs = g.Max(t => t.TotalDurationMs),
                    P95DurationMs = sorted[p95Index].TotalDurationMs,
                    LastSeen = g.Max(t => t.StartTime)
                };
            })
            .OrderByDescending(s => s.CallCount)
            .ToList();

        var model = new TracesViewModel
        {
            Traces = traces,
            ProcedureStats = procedureStats,
            ActiveTab = tab ?? "overview",
            FilterProcedure = procedure,
            ErrorsOnly = errorsOnly ?? false,
            MinDurationMs = minDuration
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Traces", model);
    }

    [HttpGet("/console/traces/{traceId}")]
    public async Task<IActionResult> TraceDetail(string traceId, CancellationToken ct)
    {
        var traces = await ExecuteAsync<TracesResult>("sp_traces", new { TraceId = traceId }, ct);

        var model = new TraceDetailViewModel
        {
            TraceId = traceId,
            Spans = traces.Spans
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("TraceDetail", model);
    }

    // --- Helpers ---

    private async Task<IActionResult> BackupsPageWithJob(string jobId, CancellationToken ct)
    {
        var backups = await ExecuteAsync<List<BackupListItem>>("sp_backup_list", ct: ct);
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        var status = await ExecuteAsync<BackupJobStatus>("sp_backup_status", new { JobId = jobId }, ct);
        var model = new BackupsViewModel
        {
            Backups = backups,
            Databases = databases,
            ActiveTab = "backups",
            ActiveJob = status
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Backups", model);
    }

    private async Task<IActionResult> BackupsTableWithJob(string jobId, CancellationToken ct)
    {
        var backups = await ExecuteAsync<List<BackupListItem>>("sp_backup_list", ct: ct);
        var status = await ExecuteAsync<BackupJobStatus>("sp_backup_status", new { JobId = jobId }, ct);
        var model = new BackupsViewModel { Backups = backups, ActiveJob = status };
        return PartialView("_BackupsTable", model);
    }

    private async Task<IActionResult> BackupsPageWithError(string error, CancellationToken ct)
    {
        var backups = await ExecuteAsync<List<BackupListItem>>("sp_backup_list", ct: ct);
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        var model = new BackupsViewModel
        {
            Backups = backups,
            Databases = databases,
            ActiveTab = "backups",
            ErrorMessage = error
        };
        await PopulateLayout(null, ct);
        return PageOrPartial("Backups", model);
    }

    private class UploadChunkPayload
    {
        public string? BackupId { get; set; }
        public string Data { get; set; } = "";
        public long Offset { get; set; }
        public long TotalSize { get; set; }
    }
}
