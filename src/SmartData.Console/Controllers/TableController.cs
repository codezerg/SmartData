using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Contracts;
using SmartData.Server;

namespace SmartData.Console.Controllers;

public class TableController : ConsoleBaseController
{
    public TableController(IAuthenticatedProcedureService procedureService) : base(procedureService) { }

    [HttpGet("/console/db/{db}/tables/{table}")]
    [HttpGet("/console/db/{db}/tables/{table}/data")]
    public async Task<IActionResult> TableData(string db, string table, int limit = 50, int offset = 0, string? orderBy = null, string? search = null, CancellationToken ct = default)
    {
        string? where = null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var schema = await ExecuteAsync<TableDescribeResult>("sp_table_describe", new { Database = db, Name = table }, ct);
            where = BuildSearchFilter(search, schema.Columns.Select(c => c.Name).ToList());
        }

        var rows = await ExecuteAsync<List<Dictionary<string, object?>>>("sp_select",
            new { Database = db, Table = table, Limit = limit, Offset = offset, OrderBy = orderBy, Where = where }, ct);

        var model = new DataGridViewModel
        {
            Db = db,
            Table = table,
            Rows = rows,
            Columns = rows.Count > 0 ? rows[0].Keys.ToList() : [],
            Offset = offset,
            Limit = limit,
            OrderBy = orderBy,
            Search = search,
            ActiveTab = "data"
        };

        await PopulateLayout(db, ct);
        return PageOrPartial("TableData", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/grid")]
    public async Task<IActionResult> TableGrid(string db, string table, int limit = 50, int offset = 0, string? orderBy = null, string? search = null, CancellationToken ct = default)
    {
        string? where = null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var schema = await ExecuteAsync<TableDescribeResult>("sp_table_describe", new { Database = db, Name = table }, ct);
            where = BuildSearchFilter(search, schema.Columns.Select(c => c.Name).ToList());
        }

        var rows = await ExecuteAsync<List<Dictionary<string, object?>>>("sp_select",
            new { Database = db, Table = table, Limit = limit, Offset = offset, OrderBy = orderBy, Where = where }, ct);

        var model = new DataGridViewModel
        {
            Db = db,
            Table = table,
            Rows = rows,
            Columns = rows.Count > 0 ? rows[0].Keys.ToList() : [],
            Offset = offset,
            Limit = limit,
            OrderBy = orderBy,
            Search = search
        };

        return PartialView("_DataGrid", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/rows")]
    public async Task<IActionResult> TableRows(string db, string table, int limit = 50, int offset = 0, string? orderBy = null, string? search = null, CancellationToken ct = default)
    {
        string? where = null;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var schema = await ExecuteAsync<TableDescribeResult>("sp_table_describe", new { Database = db, Name = table }, ct);
            where = BuildSearchFilter(search, schema.Columns.Select(c => c.Name).ToList());
        }

        var rows = await ExecuteAsync<List<Dictionary<string, object?>>>("sp_select",
            new { Database = db, Table = table, Limit = limit, Offset = offset, OrderBy = orderBy, Where = where }, ct);

        var model = new DataGridViewModel
        {
            Db = db,
            Table = table,
            Rows = rows,
            Columns = rows.Count > 0 ? rows[0].Keys.ToList() : [],
            Offset = offset,
            Limit = limit,
            OrderBy = orderBy,
            Search = search
        };

        return PartialView("_MoreRows", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/schema")]
    public async Task<IActionResult> Schema(string db, string table, CancellationToken ct)
    {
        var schema = await ExecuteAsync<TableDescribeResult>("sp_table_describe", new { Database = db, Name = table }, ct);

        var model = new SchemaViewModel
        {
            Db = db,
            Table = table,
            Columns = schema.Columns,
            Indexes = schema.Indexes
        };

        await PopulateLayout(db, ct);
        return PageOrPartial("Schema", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/query")]
    public async Task<IActionResult> Query(string db, string table, CancellationToken ct)
    {
        var schema = await ExecuteAsync<TableDescribeResult>("sp_table_describe", new { Database = db, Name = table }, ct);

        var model = new QueryViewModel
        {
            Db = db,
            Table = table,
            ColumnNames = schema.Columns.Select(c => c.Name).ToList(),
            ColumnTypes = schema.Columns.ToDictionary(c => c.Name, c => c.Type.ToUpperInvariant())
        };

        await PopulateLayout(db, ct);
        return PageOrPartial("Query", model);
    }

    [HttpPost("/console/db/{db}/tables/{table}/query/execute")]
    public async Task<IActionResult> QueryExecute(string db, string table, [FromForm] string? filter, [FromForm] string? orderByCol, [FromForm] string? orderByDir, [FromForm] int limit = 100, CancellationToken ct = default)
    {
        string? orderBy = !string.IsNullOrEmpty(orderByCol) ? $"{orderByCol}:{orderByDir}" : null;
        string? where = !string.IsNullOrWhiteSpace(filter) && filter != "{}" ? filter : null;

        var model = new QueryResultViewModel();

        try
        {
            var rows = await ExecuteAsync<List<Dictionary<string, object?>>>("sp_select",
                new { Database = db, Table = table, Limit = limit, Offset = 0, OrderBy = orderBy, Where = where }, ct);
            model.Rows = rows;
            model.Columns = rows.Count > 0 ? rows[0].Keys.ToList() : [];
            model.Filter = where;
        }
        catch (Exception ex)
        {
            model.Error = ex.Message;
        }

        return PartialView("_QueryResults", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/export")]
    public async Task<IActionResult> Export(string db, string table, CancellationToken ct)
    {
        var model = new ExportViewModel { Db = db, Table = table };
        await PopulateLayout(db, ct);
        return PageOrPartial("Export", model);
    }

    [HttpGet("/console/db/{db}/tables/{table}/export/download")]
    public async Task<IActionResult> ExportDownload(string db, string table, CancellationToken ct)
    {
        var export = await ExecuteAsync<DataExportResult>("sp_data_export", new { Database = db, Table = table }, ct);
        var json = JsonSerializer.Serialize(export.Rows, new JsonSerializerOptions { WriteIndented = true });
        var bytes = Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", $"{table}.json");
    }

    [HttpGet("/console/db/{db}/tables/{table}/import")]
    public async Task<IActionResult> Import(string db, string table, CancellationToken ct)
    {
        var model = new ImportViewModel { Db = db, Table = table };
        await PopulateLayout(db, ct);
        return PageOrPartial("Import", model);
    }

    [HttpPost("/console/db/{db}/tables/{table}/import/preview")]
    public async Task<IActionResult> ImportPreview(string db, string table, IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return PartialView("_ImportError", (object)"No file selected.");

        string json;
        using (var reader = new StreamReader(file.OpenReadStream()))
            json = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(json))
            return PartialView("_ImportError", (object)"File is empty.");

        DataImportPreview preview;
        try
        {
            preview = await ExecuteAsync<DataImportPreview>("sp_data_import",
                new { Database = db, Table = table, Rows = json, DryRun = true }, ct);
        }
        catch (Exception ex)
        {
            return PartialView("_ImportError", (object)$"Invalid file: {ex.Message}");
        }

        var schema = await ExecuteAsync<TableDescribeResult>("sp_table_describe", new { Database = db, Name = table }, ct);

        var model = new ImportPreviewViewModel
        {
            Db = db,
            Table = table,
            Json = json,
            RowCount = preview.Rows,
            FileColumns = preview.Columns,
            TableColumns = schema.Columns,
            TableColumnNames = schema.Columns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

        return PartialView("_ImportPreview", model);
    }

    [HttpPost("/console/db/{db}/tables/{table}/import/run")]
    public async Task<IActionResult> ImportRun(string db, string table, [FromForm] string? data, [FromForm] string? mode, [FromForm] string? truncate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(data))
            return PartialView("_ImportError", (object)"No data to import.");

        if (string.IsNullOrEmpty(mode)) mode = "insert";
        var doTruncate = truncate == "true";

        try
        {
            var result = await ExecuteAsync<DataImportResult>("sp_data_import",
                new { Database = db, Table = table, Rows = data, Mode = mode, Truncate = doTruncate }, ct);

            var parts = new List<string>();
            if (result.Deleted > 0) parts.Add($"{result.Deleted} existing row(s) deleted");
            if (result.Inserted > 0) parts.Add($"{result.Inserted} row(s) inserted");
            if (result.Replaced > 0) parts.Add($"{result.Replaced} row(s) replaced");
            if (result.Skipped > 0) parts.Add($"{result.Skipped} row(s) skipped (duplicates)");
            if (parts.Count == 0) parts.Add("No changes made");

            var model = new ImportResultViewModel { Success = true, Message = string.Join(", ", parts) + "." };
            return PartialView("_ImportResult", model);
        }
        catch (Exception ex)
        {
            return PartialView("_ImportError", (object)ex.Message);
        }
    }

    private static string? BuildSearchFilter(string? search, List<string>? columns)
    {
        if (string.IsNullOrWhiteSpace(search) || columns == null || columns.Count == 0)
            return null;

        var orClauses = columns.Select(col =>
            new Dictionary<string, object>
            {
                [col] = new Dictionary<string, object> { ["$contains"] = search }
            });

        var filter = new Dictionary<string, object>
        {
            ["$or"] = orClauses.ToArray()
        };

        return JsonSerializer.Serialize(filter);
    }
}
