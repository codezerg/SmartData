using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpLogs : SystemStoredProcedure<List<LogEntry>>
{
    public int Limit { get; set; } = 50;

    public override List<LogEntry> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.ServerLogs);
        db.UseDatabase("master");

        var logs = db.GetTable<SysLog>()
            .OrderByDescending(l => l.Id)
            .Take(Limit)
            .ToList();

        return logs.Select(l => new LogEntry
        {
            Type = l.Type,
            ProcedureName = l.ProcedureName,
            Message = l.Message,
            CreatedAt = l.CreatedAt
        }).ToList();
    }
}
