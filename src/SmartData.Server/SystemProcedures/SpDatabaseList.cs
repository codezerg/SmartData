using SmartData.Contracts;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;

namespace SmartData.Server.SystemProcedures;

internal class SpDatabaseList : SystemStoredProcedure<List<DatabaseListItem>>
{
    public override List<DatabaseListItem> Execute(RequestIdentity identity, IDatabaseContext db, IDatabaseProvider provider, CancellationToken ct)
    {
        identity.Require(Permissions.DatabaseList);

        var databases = provider.ListDatabases().Select(name =>
        {
            var info = provider.GetDatabaseInfo(name);
            return new DatabaseListItem
            {
                Name = name,
                Size = info.Size,
                CreatedAt = info.CreatedAt,
                ModifiedAt = info.ModifiedAt
            };
        }).ToList();

        return databases;
    }
}
