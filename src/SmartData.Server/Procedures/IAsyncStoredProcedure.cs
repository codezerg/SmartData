namespace SmartData.Server.Procedures;

public interface IAsyncStoredProcedure
{
    Task<object> ExecuteAsync(IDatabaseContext ctx, CancellationToken ct);
}
