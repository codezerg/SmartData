using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Models;
using SmartData.Contracts;
using SmartData.Server;

namespace SmartData.Console.Controllers;

public class DatabaseController : ConsoleBaseController
{
    public DatabaseController(IAuthenticatedProcedureService procedureService) : base(procedureService) { }

    [HttpGet("/console/db/{db}")]
    public async Task<IActionResult> Index(string db, CancellationToken ct)
    {
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        var tables = await ExecuteAsync<List<TableListItem>>("sp_table_list", new { Database = db }, ct);
        var dbInfo = databases.FirstOrDefault(d => d.Name == db);

        var model = new DatabaseViewModel
        {
            Db = db,
            Size = dbInfo?.Size ?? 0,
            CreatedAt = dbInfo?.CreatedAt ?? DateTime.MinValue,
            ModifiedAt = dbInfo?.ModifiedAt ?? DateTime.MinValue,
            Tables = tables,
            ActiveTab = "details"
        };

        await PopulateLayout(db, ct);
        return PageOrPartial("Index", model);
    }

    [HttpGet("/console/db/{db}/tables")]
    public async Task<IActionResult> Tables(string db, CancellationToken ct)
    {
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        var tables = await ExecuteAsync<List<TableListItem>>("sp_table_list", new { Database = db }, ct);
        var dbInfo = databases.FirstOrDefault(d => d.Name == db);

        var model = new DatabaseViewModel
        {
            Db = db,
            Size = dbInfo?.Size ?? 0,
            CreatedAt = dbInfo?.CreatedAt ?? DateTime.MinValue,
            ModifiedAt = dbInfo?.ModifiedAt ?? DateTime.MinValue,
            Tables = tables,
            ActiveTab = "tables"
        };

        await PopulateLayout(db, ct);
        return PageOrPartial("Index", model);
    }

    [HttpGet("/console/db/{db}/backups")]
    public async Task<IActionResult> Backups(string db, CancellationToken ct)
    {
        var databases = await ExecuteAsync<List<DatabaseListItem>>("sp_database_list", ct: ct);
        var allBackups = await ExecuteAsync<List<BackupListItem>>("sp_backup_list", ct: ct);
        var tables = await ExecuteAsync<List<TableListItem>>("sp_table_list", new { Database = db }, ct);
        var dbInfo = databases.FirstOrDefault(d => d.Name == db);
        var dbBackups = allBackups.Where(b => b.Databases.Contains(db, StringComparer.OrdinalIgnoreCase)).ToList();

        var model = new DatabaseViewModel
        {
            Db = db,
            Size = dbInfo?.Size ?? 0,
            CreatedAt = dbInfo?.CreatedAt ?? DateTime.MinValue,
            ModifiedAt = dbInfo?.ModifiedAt ?? DateTime.MinValue,
            Tables = tables,
            ActiveTab = "backups",
            Backups = dbBackups
        };

        await PopulateLayout(db, ct);
        return PageOrPartial("Index", model);
    }
}
