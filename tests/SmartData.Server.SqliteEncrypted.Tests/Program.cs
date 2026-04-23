using LinqToDB;
using LinqToDB.Data;
using LinqToDB.Mapping;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartData;
using SmartData.Server.Procedures;
using SmartData.Server.Providers;
using SmartData.Server.SqliteEncrypted;

namespace SmartData.Server.SqliteEncrypted.Tests;

[Table]
public class Gadget
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }
    [Column] public string Name { get; set; } = "";
    [Column] public int Quantity { get; set; }
}

public static class Program
{
    // 64-char hex strings (raw 32-byte SQLCipher keys). Different so a leak
    // between key states fails loud.
    private const string KeyA = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";
    private const string KeyB = "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

    // Tests share one app + master.db. SchemaManager<T>._ensured is a static
    // cache keyed by "db::table" — it doesn't include the directory, so
    // recreating the app in a fresh temp dir per test silently skips table
    // creation and every query errors with "no such table". One shared app
    // sidesteps that entirely. Rekey tests that mutate the key run last.
    private static WebApplication _app = null!;
    private static string _dataDir = null!;
    private static string _masterPath = null!;

    public static async Task<int> Main()
    {
        var failures = 0;

        // --- Standalone (no shared app) ---

        failures += Case("Ctor throws when EncryptionKey is empty", () =>
        {
            ExpectThrows<InvalidOperationException>(() => NewProvider(key: "", useRawHex: false));
        });

        failures += Case("Ctor throws when UseRawHexKey=true with non-hex key", () =>
        {
            ExpectThrows<ArgumentException>(() => NewProvider(key: "not-hex", useRawHex: true));
        });

        failures += Case("FormatKey raw-hex emits PRAGMA-safe quoted form", () =>
        {
            var s = SqliteEncryptedDatabaseProvider.FormatKey(KeyA, useRawHex: true);
            Expect(s == $"\"x'{KeyA}'\"", $"unexpected format: {s}");
        });

        failures += Case("FormatKey passphrase doubles single quotes", () =>
        {
            var s = SqliteEncryptedDatabaseProvider.FormatKey("it's", useRawHex: false);
            Expect(s == "'it''s'", $"unexpected escape: {s}");
        });

        // --- Shared app ---

        _dataDir = TempDir();
        _masterPath = Path.Combine(_dataDir, "master.db");
        _app = BuildApp(_dataDir, KeyA, useRawHex: true);
        _app.UseSmartData();

        try
        {
            failures += await CaseAsync("SQLCipher bundle is loaded (cipher_version pragma)", async () =>
            {
                var provider = _app.Services.GetRequiredService<IDatabaseProvider>();
                using var conn = provider.OpenConnection("master");
                var version = conn.Query<string>("SELECT sqlite_version();").FirstOrDefault();
                Expect(!string.IsNullOrEmpty(version), $"sqlite_version returned null/empty");

                // cipher_version is the definitive SQLCipher marker.
                var cipher = conn.Query<string?>("PRAGMA cipher_version;").FirstOrDefault();
                Expect(!string.IsNullOrEmpty(cipher),
                    $"PRAGMA cipher_version returned null/empty — SQLCipher bundle not active.");
                await Task.CompletedTask;
            });

            failures += await CaseAsync("Round-trip: insert then read under the active key", async () =>
            {
                await using var scope = _app.Services.CreateAsyncScope();
                var ctx = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
                ctx.UseDatabase("master");
                await ctx.InsertAsync(new Gadget { Name = "roundtrip", Quantity = 7 });
                var rows = await ctx.GetTable<Gadget>().Where(g => g.Name == "roundtrip").ToListAsync();
                Expect(rows.Count == 1 && rows[0].Quantity == 7, $"unexpected rows: {rows.Count}");
            });

            failures += await CaseAsync("File is unreadable under a wrong raw key", async () =>
            {
                // The shared app wrote to master.db under KeyA. Open the file raw
                // with KeyB and prove the first read fails.
                SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_masterPath}"));

                using var raw = new SqliteConnection($"Data Source={_masterPath};Pooling=False");
                raw.Open();
                using (var keyCmd = raw.CreateCommand())
                {
                    keyCmd.CommandText = $"PRAGMA key = \"x'{KeyB}'\";";
                    keyCmd.ExecuteNonQuery();
                }
                ExpectThrows<SqliteException>(() =>
                {
                    using var cmd = raw.CreateCommand();
                    cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
                    cmd.ExecuteScalar();
                });
                await Task.CompletedTask;
            });

            failures += await CaseAsync("Rekey via procedure: wrong CurrentKey returns error 1003; data stays", async () =>
            {
                await using (var scope = _app.Services.CreateAsyncScope())
                {
                    var ctx = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
                    ctx.UseDatabase("master");
                    await ctx.InsertAsync(new Gadget { Name = "guarded", Quantity = 1 });
                }

                var procs = _app.Services.GetRequiredService<IProcedureService>();
                ProcedureException? caught = null;
                try
                {
                    await procs.ExecuteAsync<RekeyResult>("usp_database_rekey", new
                    {
                        DbName = "master",
                        CurrentKey = "wrong-key",
                        NewKey = KeyB,
                        NewUseRawHexKey = true,
                    });
                }
                catch (ProcedureException ex) { caught = ex; }

                Expect(caught != null, "expected ProcedureException");
                Expect(caught!.MessageId == 1003,
                    $"expected MessageId=1003, got {caught.MessageId}: {caught.Message}");

                // Data still readable under KeyA (options unchanged).
                await using (var scope = _app.Services.CreateAsyncScope())
                {
                    var ctx = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
                    ctx.UseDatabase("master");
                    var rows = await ctx.GetTable<Gadget>().Where(g => g.Name == "guarded").ToListAsync();
                    Expect(rows.Count == 1, $"data disturbed: {rows.Count} rows");
                }
            });

            // MUTATING — must run last. Rotates the active key KeyA → KeyB.
            failures += await CaseAsync("Rekey via procedure: current-key match rotates key; data intact", async () =>
            {
                await using (var scope = _app.Services.CreateAsyncScope())
                {
                    var ctx = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
                    ctx.UseDatabase("master");
                    await ctx.InsertAsync(new Gadget { Name = "rotate-me", Quantity = 5 });
                }

                var procs = _app.Services.GetRequiredService<IProcedureService>();
                var result = await procs.ExecuteAsync<RekeyResult>("usp_database_rekey", new
                {
                    DbName = "master",
                    CurrentKey = KeyA,
                    NewKey = KeyB,
                    NewUseRawHexKey = true,
                });
                Expect(result.Success && result.DbName == "master", "procedure did not return Success");

                // Same provider now speaks under KeyB; all prior data still visible.
                await using (var scope = _app.Services.CreateAsyncScope())
                {
                    var ctx = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
                    ctx.UseDatabase("master");
                    var rotate = await ctx.GetTable<Gadget>().Where(g => g.Name == "rotate-me").ToListAsync();
                    Expect(rotate.Count == 1, $"rotate-me missing after rekey: {rotate.Count}");
                    var roundtrip = await ctx.GetTable<Gadget>().Where(g => g.Name == "roundtrip").ToListAsync();
                    Expect(roundtrip.Count == 1, $"pre-rekey rows missing: {roundtrip.Count}");
                }

                // Old key no longer opens the DB.
                SqliteConnection.ClearPool(new SqliteConnection($"Data Source={_masterPath}"));
                using var raw = new SqliteConnection($"Data Source={_masterPath};Pooling=False");
                raw.Open();
                using (var keyCmd = raw.CreateCommand())
                {
                    keyCmd.CommandText = $"PRAGMA key = \"x'{KeyA}'\";";
                    keyCmd.ExecuteNonQuery();
                }
                ExpectThrows<SqliteException>(() =>
                {
                    using var cmd = raw.CreateCommand();
                    cmd.CommandText = "SELECT count(*) FROM sqlite_master;";
                    cmd.ExecuteScalar();
                });
            });
        }
        finally
        {
            await _app.DisposeAsync();
            Cleanup(_dataDir);
        }

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
        return failures;
    }

    // --- Helpers ---

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"smartdata-enc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }

    private static WebApplication BuildApp(string dataDir, string key, bool useRawHex)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSmartData();
        builder.Services.AddSmartDataSqliteEncrypted(o =>
        {
            o.DataDirectory = dataDir;
            o.EncryptionKey = key;
            o.UseRawHexKey = useRawHex;
        });
        return builder.Build();
    }

    private static SqliteEncryptedDatabaseProvider NewProvider(string key, bool useRawHex) =>
        new(Options.Create(new SqliteEncryptedDatabaseOptions
        {
            DataDirectory = TempDir(),
            EncryptionKey = key,
            UseRawHexKey = useRawHex,
        }));

    private static int Case(string name, Action body)
    {
        try { body(); Console.WriteLine($"  PASS  {name}"); return 0; }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CaseAsync(string name, Func<Task> body)
    {
        try { await body(); Console.WriteLine($"  PASS  {name}"); return 0; }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL  {name}");
            Console.WriteLine($"        {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectThrows<TException>(Action body) where TException : Exception
    {
        try { body(); }
        catch (TException) { return; }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"expected {typeof(TException).Name}, got {ex.GetType().Name}: {ex.Message}");
        }
        throw new InvalidOperationException($"expected {typeof(TException).Name}, nothing thrown");
    }
}
