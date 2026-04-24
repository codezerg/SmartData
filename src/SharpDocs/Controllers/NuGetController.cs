using Microsoft.AspNetCore.Mvc;
using SharpDocs.Models;
using SharpDocs.Services;

namespace SharpDocs.Controllers;

[Route("v3")]
public sealed class NuGetController : Controller
{
    private readonly NuGetFeed _feed;
    public NuGetController(NuGetFeed feed) { _feed = feed; }

    private string BaseUrl() => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/v3";

    // GET /v3/index.json — service index
    [HttpGet("index.json")]
    public IActionResult ServiceIndex()
    {
        var baseUrl = BaseUrl();
        Dictionary<string, object?> Res(string url, string type) => new()
        {
            ["@id"] = url,
            ["@type"] = type,
        };
        return Json(new Dictionary<string, object?>
        {
            ["version"] = "3.0.0",
            ["resources"] = new object[]
            {
                Res($"{baseUrl}/flatcontainer/", "PackageBaseAddress/3.0.0"),
                Res($"{baseUrl}/registration/",  "RegistrationsBaseUrl/3.6.0"),
                Res($"{baseUrl}/registration/",  "RegistrationsBaseUrl/Versioned"),
                Res($"{baseUrl}/search",         "SearchQueryService"),
                Res($"{baseUrl}/search",         "SearchQueryService/3.0.0-beta"),
                Res($"{baseUrl}/search",         "SearchQueryService/3.0.0-rc"),
                Res($"{baseUrl}/autocomplete",   "SearchAutocompleteService"),
                Res($"{baseUrl}/autocomplete",   "SearchAutocompleteService/3.0.0-rc"),
            },
        });
    }

    // GET /v3/flatcontainer/{id}/index.json
    [HttpGet("flatcontainer/{id}/index.json")]
    public IActionResult FlatContainerVersions(string id)
    {
        var pkgs = _feed.GetVersions(id);
        if (pkgs.Count == 0) return NotFound();
        return Json(new { versions = pkgs.Select(p => p.Version.ToLowerInvariant()).ToArray() });
    }

    // GET /v3/flatcontainer/{id}/{version}/{id}.{version}.nupkg
    [HttpGet("flatcontainer/{id}/{version}/{file}.nupkg")]
    public IActionResult FlatContainerNupkg(string id, string version, string file)
    {
        var pkg = _feed.Get(id, version);
        if (pkg == null) return NotFound();
        var expected = $"{id}.{version}".ToLowerInvariant();
        if (!string.Equals(file, expected, StringComparison.OrdinalIgnoreCase)) return NotFound();
        return PhysicalFile(pkg.NupkgPath, "application/octet-stream");
    }

    // GET /v3/flatcontainer/{id}/{version}/{id}.nuspec
    [HttpGet("flatcontainer/{id}/{version}/{file}.nuspec")]
    public IActionResult FlatContainerNuspec(string id, string version, string file)
    {
        var pkg = _feed.Get(id, version);
        if (pkg == null) return NotFound();
        if (!string.Equals(file, id, StringComparison.OrdinalIgnoreCase)) return NotFound();

        using var zip = System.IO.Compression.ZipFile.OpenRead(pkg.NupkgPath);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains('/') && !e.FullName.Contains('\\'));
        if (entry == null) return NotFound();

        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return File(ms.ToArray(), "application/xml");
    }

    // GET /v3/registration/{id}/index.json
    [HttpGet("registration/{id}/index.json")]
    public IActionResult Registration(string id)
    {
        var pkgs = _feed.GetVersions(id);
        if (pkgs.Count == 0) return NotFound();

        var baseUrl = BaseUrl();
        var regUrl = $"{baseUrl}/registration/{id.ToLowerInvariant()}/index.json";

        var leaves = pkgs.Select(p => BuildLeaf(p, baseUrl, regUrl)).ToArray();

        var page = new Dictionary<string, object?>
        {
            ["@id"] = regUrl + "#page",
            ["count"] = leaves.Length,
            ["lower"] = pkgs[0].Version.ToLowerInvariant(),
            ["upper"] = pkgs[^1].Version.ToLowerInvariant(),
            ["items"] = leaves,
        };

        return Json(new Dictionary<string, object?>
        {
            ["count"] = 1,
            ["items"] = new object[] { page },
        });
    }

    private static Dictionary<string, object?> BuildLeaf(NuGetPackage p, string baseUrl, string regUrl)
    {
        var idLower = p.Id.ToLowerInvariant();
        var verLower = p.Version.ToLowerInvariant();
        var leafId = $"{regUrl}#{verLower}";
        var packageContent = $"{baseUrl}/flatcontainer/{idLower}/{verLower}/{idLower}.{verLower}.nupkg";

        var depGroups = p.DependencyGroups.Select(g => new Dictionary<string, object?>
        {
            ["@id"] = leafId + "/deps",
            ["@type"] = "PackageDependencyGroup",
            ["targetFramework"] = g.TargetFramework,
            ["dependencies"] = g.Dependencies.Select(d => new Dictionary<string, object?>
            {
                ["@id"] = leafId + "/dep",
                ["@type"] = "PackageDependency",
                ["id"] = d.Id,
                ["range"] = d.Range ?? "",
            }).ToArray(),
        }).ToArray();

        var catalogEntry = new Dictionary<string, object?>
        {
            ["@id"] = leafId + "/catalog",
            ["@type"] = "PackageDetails",
            ["authors"] = p.Authors ?? "",
            ["dependencyGroups"] = depGroups,
            ["description"] = p.Description ?? "",
            ["iconUrl"] = "",
            ["id"] = p.Id,
            ["licenseExpression"] = p.LicenseExpression ?? "",
            ["licenseUrl"] = "",
            ["listed"] = true,
            ["packageContent"] = packageContent,
            ["projectUrl"] = p.ProjectUrl ?? "",
            ["published"] = p.Published,
            ["requireLicenseAcceptance"] = false,
            ["summary"] = "",
            ["tags"] = p.Tags,
            ["title"] = p.Id,
            ["version"] = p.Version,
        };

        return new Dictionary<string, object?>
        {
            ["@id"] = leafId,
            ["@type"] = new[] { "Package", "http://schema.nuget.org/catalog#Permalink" },
            ["commitId"] = "00000000-0000-0000-0000-000000000000",
            ["commitTimeStamp"] = p.Published,
            ["catalogEntry"] = catalogEntry,
            ["packageContent"] = packageContent,
            ["registration"] = regUrl,
        };
    }

    // GET /v3/search?q=&skip=&take=&prerelease=
    [HttpGet("search")]
    public IActionResult Search(string? q, int skip = 0, int take = 20, bool prerelease = true)
    {
        var baseUrl = BaseUrl();
        IEnumerable<NuGetPackage> matches = _feed.LatestPerId();
        if (!string.IsNullOrWhiteSpace(q))
        {
            matches = matches.Where(p =>
                p.Id.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                p.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }
        var list = matches.ToList();

        var data = list.Skip(skip).Take(take).Select(p =>
        {
            var idLower = p.Id.ToLowerInvariant();
            var regUrl = $"{baseUrl}/registration/{idLower}/index.json";
            var versions = _feed.GetVersions(p.Id);
            return new Dictionary<string, object?>
            {
                ["@id"] = regUrl,
                ["@type"] = "Package",
                ["registration"] = regUrl,
                ["id"] = p.Id,
                ["version"] = p.Version,
                ["description"] = p.Description ?? "",
                ["summary"] = "",
                ["title"] = p.Id,
                ["iconUrl"] = "",
                ["licenseUrl"] = "",
                ["projectUrl"] = p.ProjectUrl ?? "",
                ["tags"] = p.Tags,
                ["authors"] = new[] { p.Authors ?? "" },
                ["totalDownloads"] = 0,
                ["verified"] = false,
                ["versions"] = versions.Select(v => new Dictionary<string, object?>
                {
                    ["version"] = v.Version,
                    ["downloads"] = 0,
                    ["@id"] = $"{regUrl}#{v.Version.ToLowerInvariant()}",
                }).ToArray(),
            };
        }).ToArray();

        return Json(new { totalHits = list.Count, data });
    }

    // GET /v3/autocomplete?q=&take=
    [HttpGet("autocomplete")]
    public IActionResult Autocomplete(string? q, int take = 20)
    {
        var ids = _feed.AllIds();
        if (!string.IsNullOrWhiteSpace(q))
            ids = ids.Where(i => i.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        var page = ids.Take(take).ToArray();
        return Json(new { totalHits = ids.Count, data = page });
    }
}
