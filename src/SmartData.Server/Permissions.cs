using System.ComponentModel;
using System.Reflection;

namespace SmartData.Server;

public static class Permissions
{
    // ── System: Global permissions not scoped to any database ──

    [Description("Full database access")]
    public const string DatabaseAll = "Database:*";

    [Description("Create databases")]
    public const string DatabaseCreate = "Database:Create";

    [Description("Drop databases")]
    public const string DatabaseDrop = "Database:Drop";

    [Description("List databases")]
    public const string DatabaseList = "Database:List";

    [Description("Full backup access")]
    public const string BackupAll = "Backup:*";

    [Description("Create backups")]
    public const string BackupCreate = "Backup:Create";

    [Description("Drop backups")]
    public const string BackupDrop = "Backup:Drop";

    [Description("List backups")]
    public const string BackupList = "Backup:List";

    [Description("Restore backups")]
    public const string BackupRestore = "Backup:Restore";

    [Description("Download backups")]
    public const string BackupDownload = "Backup:Download";

    [Description("Upload backups")]
    public const string BackupUpload = "Backup:Upload";

    [Description("View backup history")]
    public const string BackupHistory = "Backup:History";

    [Description("Full user access")]
    public const string UserAll = "User:*";

    [Description("Create users")]
    public const string UserCreate = "User:Create";

    [Description("Grant permissions")]
    public const string UserGrant = "User:Grant";

    [Description("Revoke permissions")]
    public const string UserRevoke = "User:Revoke";

    [Description("Delete users")]
    public const string UserDelete = "User:Delete";

    [Description("List user permissions")]
    public const string UserList = "User:List";

    [Description("Full server access")]
    public const string ServerAll = "Server:*";

    [Description("View storage info")]
    public const string ServerStorage = "Server:Storage";

    [Description("View server logs")]
    public const string ServerLogs = "Server:Logs";

    [Description("View server errors")]
    public const string ServerErrors = "Server:Errors";

    [Description("View server metrics")]
    public const string ServerMetrics = "Server:Metrics";

    [Description("Manage server settings")]
    public const string ServerSettings = "Server:Settings";

    [Description("Full scheduler access")]
    public const string SchedulerAll = "Scheduler:*";

    [Description("List and inspect schedules")]
    public const string SchedulerList = "Scheduler:List";

    [Description("Edit schedules (timing, retry, enabled)")]
    public const string SchedulerEdit = "Scheduler:Edit";

    [Description("Manually trigger scheduled runs")]
    public const string SchedulerRun = "Scheduler:Run";

    [Description("Cancel in-flight scheduled runs")]
    public const string SchedulerCancel = "Scheduler:Cancel";

    // ── Scoped: Per-database permissions (prefixed at runtime with db name or *) ──

    [Description("Full table access")]
    public const string TableAll = "Table:*";

    [Description("Create tables")]
    public const string TableCreate = "Table:Create";

    [Description("Drop tables")]
    public const string TableDrop = "Table:Drop";

    [Description("List tables")]
    public const string TableList = "Table:List";

    [Description("Describe table schema")]
    public const string TableDescribe = "Table:Describe";

    [Description("Rename tables")]
    public const string TableRename = "Table:Rename";

    [Description("Full column access")]
    public const string ColumnAll = "Column:*";

    [Description("Add columns")]
    public const string ColumnAdd = "Column:Add";

    [Description("Drop columns")]
    public const string ColumnDrop = "Column:Drop";

    [Description("List columns")]
    public const string ColumnList = "Column:List";

    [Description("Rename columns")]
    public const string ColumnRename = "Column:Rename";

    [Description("Full index access")]
    public const string IndexAll = "Index:*";

    [Description("Create indexes")]
    public const string IndexCreate = "Index:Create";

    [Description("Drop indexes")]
    public const string IndexDrop = "Index:Drop";

    [Description("List indexes")]
    public const string IndexList = "Index:List";

    [Description("Full data access")]
    public const string DataAll = "Data:*";

    [Description("Query data")]
    public const string DataSelect = "Data:Select";

    [Description("Insert data")]
    public const string DataInsert = "Data:Insert";

    [Description("Update data")]
    public const string DataUpdate = "Data:Update";

    [Description("Delete data")]
    public const string DataDelete = "Data:Delete";

    [Description("Export data")]
    public const string DataExport = "Data:Export";

    [Description("Import data")]
    public const string DataImport = "Data:Import";

    [Description("Dump raw data")]
    public const string DataDump = "Data:Dump";

    [Description("Full ledger access")]
    public const string LedgerAll = "Ledger:*";

    [Description("Read the ledger and its digest")]
    public const string LedgerRead = "Ledger:Read";

    [Description("Verify a ledger chain against anchors")]
    public const string LedgerVerify = "Ledger:Verify";

    // ── Collections ──

    public static readonly Permission[] System = BuildPermissions(
        nameof(DatabaseAll), nameof(DatabaseCreate), nameof(DatabaseDrop), nameof(DatabaseList),
        nameof(BackupAll), nameof(BackupCreate), nameof(BackupDrop), nameof(BackupList),
        nameof(BackupRestore), nameof(BackupDownload), nameof(BackupUpload), nameof(BackupHistory),
        nameof(UserAll), nameof(UserCreate), nameof(UserGrant), nameof(UserRevoke),
        nameof(UserDelete), nameof(UserList),
        nameof(ServerAll), nameof(ServerStorage), nameof(ServerLogs), nameof(ServerErrors),
        nameof(ServerMetrics), nameof(ServerSettings),
        nameof(SchedulerAll), nameof(SchedulerList), nameof(SchedulerEdit),
        nameof(SchedulerRun), nameof(SchedulerCancel)
    );

    public static readonly Permission[] Scoped = BuildPermissions(
        nameof(TableAll), nameof(TableCreate), nameof(TableDrop), nameof(TableList),
        nameof(TableDescribe), nameof(TableRename),
        nameof(ColumnAll), nameof(ColumnAdd), nameof(ColumnDrop), nameof(ColumnList),
        nameof(ColumnRename),
        nameof(IndexAll), nameof(IndexCreate), nameof(IndexDrop), nameof(IndexList),
        nameof(DataAll), nameof(DataSelect), nameof(DataInsert), nameof(DataUpdate),
        nameof(DataDelete), nameof(DataExport), nameof(DataImport), nameof(DataDump),
        nameof(LedgerAll), nameof(LedgerRead), nameof(LedgerVerify)
    );

    // ── Helpers ──

    private static Permission[] BuildPermissions(params string[] fieldNames)
    {
        var type = typeof(Permissions);
        var result = new Permission[fieldNames.Length];

        for (int i = 0; i < fieldNames.Length; i++)
        {
            var field = type.GetField(fieldNames[i], BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Field '{fieldNames[i]}' not found on Permissions.");

            var key = (string)field.GetValue(null)!;
            var description = field.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? throw new InvalidOperationException($"Field '{fieldNames[i]}' is missing [Description].");

            ValidateKey(fieldNames[i], key);
            result[i] = new Permission(key, description);
        }

        return result;
    }

    private static void ValidateKey(string fieldName, string key)
    {
        // "DatabaseDrop" should match "Database:Drop", "DatabaseAll" should match "Database:*"
        int split = -1;
        for (int i = 1; i < fieldName.Length; i++)
        {
            if (char.IsUpper(fieldName[i]))
            {
                split = i;
                break;
            }
        }

        if (split < 0)
            throw new InvalidOperationException($"Cannot parse field name '{fieldName}' — expected PascalCase like 'DatabaseDrop'.");

        var category = fieldName[..split];
        var action = fieldName[split..];
        var expectedKey = action == "All" ? $"{category}:*" : $"{category}:{action}";

        if (key != expectedKey)
            throw new InvalidOperationException($"Field '{fieldName}' has key '{key}' but expected '{expectedKey}'.");
    }
}
