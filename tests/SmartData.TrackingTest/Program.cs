using LinqToDB;
using LinqToDB.Mapping;
using Microsoft.Extensions.DependencyInjection;
using SmartData;
using SmartData.Server.Attributes;
using SmartData.Server.Procedures;
using SmartData.Server.Sqlite;

namespace SmartData.TrackingTest;

[Table]
[Tracked]
public class Widget
{
    [PrimaryKey, Identity]
    [Column] public int Id { get; set; }
    [Column] public string Name { get; set; } = "";
    [Column, Nullable] public string? Note { get; set; }
    [Column] public int Quantity { get; set; }
    [Column] public DateTime CreatedOn { get; set; }
}

public static class Program
{
    public static async Task<int> Main()
    {
        // Isolated temp directory per run — deterministic, no cross-run pollution.
        var dataDir = Path.Combine(Path.GetTempPath(), $"smartdata-tracking-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDir);
        Console.WriteLine($"Data dir: {dataDir}");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSmartData();
        builder.Services.AddSmartDataSqlite(o => o.DataDirectory = dataDir);

        var app = builder.Build();

        // UseSmartData wires logger hooks + initializes master DB + maps endpoints.
        // We skip app.Run — just invoke the init side-effects and use the DI container.
        app.UseSmartData();

        var failures = 0;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
            ctx.UseDatabase("master");

            failures += await Case("Insert lands in Widget_History", async () =>
            {
                var w = new Widget { Name = "alpha", Note = "first", Quantity = 10, CreatedOn = DateTime.UtcNow };
                await ctx.InsertAsync(w);
                Expect(w.Id > 0, $"identity not assigned, got Id={w.Id}");

                var hist = await ctx.History<Widget>()
                    .Where(h => h.Data.Id == w.Id && h.Operation == "I")
                    .ToListAsync();
                Expect(hist.Count == 1, $"expected 1 History row, got {hist.Count}");
                Expect(hist[0].Data.Name == "alpha", $"Data.Name='{hist[0].Data.Name}'");
                Expect(hist[0].Data.Quantity == 10, $"Data.Quantity={hist[0].Data.Quantity}");
            });

            failures += await Case("Update appends a U row", async () =>
            {
                var w = new Widget { Name = "beta", Quantity = 1, CreatedOn = DateTime.UtcNow };
                await ctx.InsertAsync(w);

                w.Quantity = 42;
                await ctx.UpdateAsync(w);

                var hist = await ctx.History<Widget>()
                    .Where(h => h.Data.Id == w.Id)
                    .OrderBy(h => h.HistoryId)
                    .ToListAsync();
                Expect(hist.Count == 2, $"expected 2 rows (I, U), got {hist.Count}");
                Expect(hist[0].Operation == "I", $"row 0 Operation='{hist[0].Operation}'");
                Expect(hist[1].Operation == "U", $"row 1 Operation='{hist[1].Operation}'");
                Expect(hist[1].Data.Quantity == 42, $"U row Data.Quantity={hist[1].Data.Quantity}");
            });

            failures += await Case("Delete appends a D row", async () =>
            {
                var w = new Widget { Name = "gamma", Quantity = 5, CreatedOn = DateTime.UtcNow };
                await ctx.InsertAsync(w);
                await ctx.DeleteAsync(w);

                var hist = await ctx.History<Widget>()
                    .Where(h => h.Data.Id == w.Id)
                    .OrderBy(h => h.HistoryId)
                    .ToListAsync();
                Expect(hist.Count == 2, $"expected 2 rows (I, D), got {hist.Count}");
                Expect(hist[^1].Operation == "D", $"last row Operation='{hist[^1].Operation}'");
            });
        }

        await app.DisposeAsync();
        try { Directory.Delete(dataDir, recursive: true); } catch { /* best-effort */ }

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
        return failures;
    }

    private static async Task<int> Case(string name, Func<Task> body)
    {
        try
        {
            await body();
            Console.WriteLine($"  PASS  {name}");
            return 0;
        }
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
}
