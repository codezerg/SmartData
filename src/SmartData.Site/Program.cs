using SharpDocs;
using SharpDocs.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharpDocs(builder.Configuration);

var app = builder.Build();

// Warm singletons so they scan their sources at startup (not on first request).
app.Services.GetRequiredService<DocsLoader>();
app.Services.GetRequiredService<SearchIndex>();
app.Services.GetRequiredService<NuGetFeed>();

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();

// Fallback catch-all for docs slugs. Registered last so explicit controller
// routes (/, /search, /packages, /v3/*) win.
app.MapControllerRoute(
    name: "docs-slug",
    pattern: "{**slug}",
    defaults: new { controller = "Docs", action = "Page" });

app.Run();
