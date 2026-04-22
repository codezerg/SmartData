using SmartData.Contracts;
using SmartData.Server.Entities;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpErrors : SystemStoredProcedure<List<LogEntry>>
{
    public string Name { get; set; } = "";
    public int Limit { get; set; } = 50;

    public override List<LogEntry> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.ServerErrors);
        db.UseDatabase("master");

        var query = db.GetTable<SysLog>()
            .Where(l => l.Type == "error" || l.Type == "compilation");

        if (!string.IsNullOrEmpty(Name))
            query = query.Where(l => l.ProcedureName == Name);

        var logs = query.OrderByDescending(l => l.Id).Take(Limit).ToList();

        return logs.Select(l => new LogEntry
        {
            Type = l.Type,
            ProcedureName = l.ProcedureName,
            Message = l.Message,
            CreatedAt = l.CreatedAt
        }).ToList();
    }
}
