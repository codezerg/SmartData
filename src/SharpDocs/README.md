# SharpDocs

Reusable ASP.NET Core site for small library projects. Serves:

- Markdown docs (with YAML frontmatter) from a folder on disk
- A **NuGet v3** feed backed by `*.nupkg` files in an artifacts folder
- Full-text search across the docs

Host app:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSharpDocs(builder.Configuration);

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();
app.MapFallbackToController("Page", "Docs");
app.Run();
```

`appsettings.json`:

```json
{
  "SharpDocs": {
    "DocsRoot": "../../docs",
    "ArtifactsRoot": "../../artifacts",
    "Branding": {
      "Title": "MyLib Docs",
      "BrandName": "MyLib",
      "GitHubUrl": "https://github.com/me/mylib",
      "NavLinks": [
        { "Label": "Packages", "Href": "/packages" },
        { "Label": "NuGet Feed", "Href": "/v3/index.json" }
      ]
    }
  }
}
```
