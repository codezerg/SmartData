using LinqToDB;
using SmartData.Core;
using SmartData.Server.Entities;
using SmartData.Server.Providers;

namespace SmartData.Server;

/// <summary>
/// Database lifecycle coordinator. Handles master database initialization,
/// database creation/drop with validation.
/// </summary>
internal class DatabaseManager
{
    private readonly IDatabaseProvider _provider;
    private const string MasterDbName = "master";
    private const string DefaultAdminUser = "admin";
    private const string DefaultAdminPassword = "admin";

    public DatabaseManager(IDatabaseProvider provider)
    {
        _provider = provider;
    }

    public void EnsureMasterDatabase()
    {
        _provider.EnsureDatabase(MasterDbName);

        SchemaManager<SysUser>.EnsureSchema(MasterDbName, _provider);
        SchemaManager<SysUserPermission>.EnsureSchema(MasterDbName, _provider);
        SchemaManager<SysSetting>.EnsureSchema(MasterDbName, _provider);
        SchemaManager<SysLog>.EnsureSchema(MasterDbName, _provider);

        using var db = _provider.OpenConnection(MasterDbName);
        if (!db.GetTable<SysUser>().Any())
        {
            db.Insert(new SysUser
            {
                Id = IdGenerator.NewId(),
                Username = DefaultAdminUser,
                PasswordHash = HashPassword(DefaultAdminPassword),
                IsAdmin = true,
                CreatedAt = DateTime.UtcNow
            });
        }
    }

    public bool DatabaseExists(string name) => _provider.DatabaseExists(name);

    public void CreateDatabase(string name)
    {
        if (string.Equals(name, MasterDbName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot create a database named 'master'.");

        if (_provider.DatabaseExists(name))
            throw new InvalidOperationException($"Database '{name}' already exists.");

        _provider.EnsureDatabase(name);
    }

    public void DropDatabase(string name)
    {
        if (string.Equals(name, MasterDbName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot drop the master database.");

        _provider.DropDatabase(name);
    }

    internal static string HashPassword(string password)
    {
        return PasswordHasher.HashPassword(password);
    }
}
