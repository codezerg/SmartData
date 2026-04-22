namespace SmartData.Server.Procedures;

public interface IStoredProcedure
{
    object Execute(IDatabaseContext ctx, CancellationToken ct);
}
