namespace SmartData.Server.Procedures;

public interface ITransaction : IDisposable
{
    void Commit();
    void Rollback();
}
