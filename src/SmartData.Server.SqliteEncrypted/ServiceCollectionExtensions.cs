using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartData.Server;
using SmartData.Server.Providers;

namespace SmartData.Server.SqliteEncrypted;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the SQLCipher-backed SQLite provider. Replaces any previously
    /// registered <see cref="IDatabaseProvider"/>. The encryption key must be
    /// set on <see cref="SqliteEncryptedDatabaseOptions"/> — the provider
    /// constructor throws if it is empty.
    /// </summary>
    /// <remarks>
    /// Cannot coexist with <c>SmartData.Server.Sqlite</c>'s <c>AddSmartDataSqlite</c>
    /// in the same process — the two providers pull conflicting
    /// <c>SQLitePCLRaw</c> bundles. Pick one.
    /// </remarks>
    public static IServiceCollection AddSmartDataSqliteEncrypted(
        this IServiceCollection services,
        Action<SqliteEncryptedDatabaseOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure<SqliteEncryptedDatabaseOptions>(o =>
        {
            o.DataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
        });
        services.Configure(configure);

        services.RemoveAll<IDatabaseProvider>();
        services.AddSingleton<SqliteEncryptedDatabaseProvider>();
        services.AddSingleton<IDatabaseProvider>(sp => sp.GetRequiredService<SqliteEncryptedDatabaseProvider>());
        services.AddSingleton<IEncryptedDatabaseMaintenance>(sp => sp.GetRequiredService<SqliteEncryptedDatabaseProvider>());

        // Auto-register usp_database_rekey (and any future procedures in this
        // assembly) so consumers don't have to call AddStoredProcedures manually.
        services.AddStoredProcedures(typeof(ServiceCollectionExtensions).Assembly);

        return services;
    }
}
