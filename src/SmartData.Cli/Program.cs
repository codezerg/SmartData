using SmartData.Cli.Commands;
using System;
using System.Collections.Generic;
using System.Text;

namespace SmartData.Cli
{
    internal class Program
    {
        public static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return;
            }

            var config = SdConfig.Load();
            var client = new ApiClient(config);
            var command = args[0].ToLowerInvariant();
            var rest = args[1..];

            var commands = new Dictionary<string, Func<Task>>
            {
                ["connect"] = () => { ConnectCommand.Run(rest, config); return Task.CompletedTask; },
                ["login"] = () => LoginCommand.Run(rest, config, client),
                ["logout"] = () => LoginCommand.Logout(config, client),
                ["status"] = () => client.SendAndPrint("sp_status"),
                ["storage"] = () => StorageCommand.Run(rest, config, client),

                ["user"] = () => UserCommand.Run(rest, config, client),

                ["db"] = () => DbCommand.Run(rest, config, client),
                ["table"] = () => TableCommand.Run(rest, config, client),
                ["column"] = () => ColumnCommand.Run(rest, config, client),
                ["index"] = () => IndexCommand.Run(rest, config, client),

                ["sp"] = () => SpCommand.Run(rest, config, client),
                ["exec"] = () => ExecCommand.Run(rest, config, client),
                ["dump"] = () => DumpCommand.Run(rest, config, client),

                ["select"] = () => DataCommand.Select(rest, config, client),
                ["insert"] = () => DataCommand.Insert(rest, config, client),
                ["update"] = () => DataCommand.Update(rest, config, client),
                ["delete"] = () => DataCommand.Delete(rest, config, client),
                ["export"] = () => ExportImportCommand.Export(rest, config, client),
                ["import"] = () => ExportImportCommand.Import(rest, config, client),

                ["settings"] = () => SettingsCommand.Run(rest, config, client),
                ["backup"] = () => BackupCommand.Run(rest, config, client),
                ["logs"] = () => LogsCommand.Run(rest, config, client),
                ["metrics"] = () => MetricsCommand.Run(rest, config, client),
            };

            if (commands.TryGetValue(command, out var handler))
                await handler();
            else
            {
                Console.Error.WriteLine($"Unknown command: {command}");
                PrintUsage();
            }
        }


        static void PrintUsage()
        {
            Console.WriteLine("Usage: sd <command> [args]");
            Console.WriteLine();
            Console.WriteLine("Server:");
            Console.WriteLine("  connect <server>                          Set active server");
            Console.WriteLine("  login [--username x] [--password y]       Authenticate");
            Console.WriteLine("  logout                                    End session");
            Console.WriteLine("  status                                    Server status");
            Console.WriteLine("  storage [--db d]                          Storage usage");
            Console.WriteLine();
            Console.WriteLine("Users:");
            Console.WriteLine("  user create --username x --password y");
            Console.WriteLine();
            Console.WriteLine("Databases:");
            Console.WriteLine("  db list                                   List databases");
            Console.WriteLine("  db create <name>                          Create database");
            Console.WriteLine("  db drop <name>                            Drop database");
            Console.WriteLine("  db use <name>                             Set active database");
            Console.WriteLine();
            Console.WriteLine("Tables:");
            Console.WriteLine("  table list                                List tables");
            Console.WriteLine("  table create <name> [--col Name:Type[:pk] ...]");
            Console.WriteLine("  table drop <name>                         Drop table");
            Console.WriteLine("  table rename <name> <new-name>            Rename table");
            Console.WriteLine("  table describe <name>                     Show columns + indexes");
            Console.WriteLine();
            Console.WriteLine("Columns:");
            Console.WriteLine("  column add <table> <name> <type> [--nullable]");
            Console.WriteLine("  column drop <table> <name>                Drop column");
            Console.WriteLine("  column rename <table> <name> <new-name>   Rename column");
            Console.WriteLine();
            Console.WriteLine("Indexes:");
            Console.WriteLine("  index list <table>                        List indexes");
            Console.WriteLine("  index create <table> <name> --columns Col1,Col2 [--unique]");
            Console.WriteLine("  index drop <table> <name>                 Drop index");
            Console.WriteLine();
            Console.WriteLine("Stored Procedures:");
            Console.WriteLine("  sp errors [name] [--limit N]              View compilation/runtime errors");
            Console.WriteLine();
            Console.WriteLine("Data:");
            Console.WriteLine("  select <table> [--where '{...}'] [--orderby Col[:desc]] [--limit N]");
            Console.WriteLine("  insert <table> --Col1 val1 --Col2 val2");
            Console.WriteLine("  update <table> --where '{...}' --set '{...}'");
            Console.WriteLine("  delete <table> --where '{...}'");
            Console.WriteLine("  export <table> [--where '{...}'] [--out file.json]");
            Console.WriteLine("  import <table> [--file data.json]");
            Console.WriteLine();
            Console.WriteLine("Execution:");
            Console.WriteLine("  exec <sp-name> [--param1 val1 ...]        Execute stored procedure");
            Console.WriteLine("  dump [--out ./dump.md]                    Dump full database schema");
            Console.WriteLine();
            Console.WriteLine("Backups:");
            Console.WriteLine("  backup create <db1,db2 or *>              Create zip backup");
            Console.WriteLine("  backup restore <backup-id> [--force]      Restore backup");
            Console.WriteLine("  backup list                               List backups");
            Console.WriteLine("  backup drop <backup-id>                   Delete backup");
            Console.WriteLine("  backup download <id> [--out file.zip]     Download backup (resumable)");
            Console.WriteLine("  backup upload --file backup.zip           Upload backup (resumable)");
            Console.WriteLine();
            Console.WriteLine("Settings:");
            Console.WriteLine("  settings list                             List all settings");
            Console.WriteLine("  settings get <key>                        Get a setting value");
            Console.WriteLine("  settings set <key> <value>                Update a setting");
            Console.WriteLine();
            Console.WriteLine("Logs:");
            Console.WriteLine("  logs [--limit N]                          View server logs");
            Console.WriteLine();
            Console.WriteLine("Metrics:");
            Console.WriteLine("  metrics list [--name X] [--type Y]        View metrics");
            Console.WriteLine("  metrics watch [--interval 5]              Live-refresh metrics");
            Console.WriteLine("  metrics traces [--procedure X] [--errors] List traces");
            Console.WriteLine("  metrics trace <traceId>                   Show span tree");
            Console.WriteLine("  metrics exceptions [--type X]             List exceptions");
        }

    }
}
