using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpDocs.Models;

namespace SharpDocs.Services;

public sealed class NuGetFeed
{
    private readonly Dictionary<string, List<NuGetPackage>> _byId; // id lowercase -> versions
    private readonly List<NuGetPackage> _all;
    private readonly string _artifactsRoot;

    public NuGetFeed(IOptions<SharpDocsOptions> options, IHostEnvironment env, ILogger<NuGetFeed> log)
    {
        _artifactsRoot = Path.IsPathRooted(options.Value.ArtifactsRoot)
            ? Path.GetFullPath(options.Value.ArtifactsRoot)
            : Path.GetFullPath(Path.Combine(env.ContentRootPath, options.Value.ArtifactsRoot));

        _byId = new(StringComparer.OrdinalIgnoreCase);
        _all = new();

        if (!Directory.Exists(_artifactsRoot))
        {
            log.LogWarning("Artifacts root not found: {Path}", _artifactsRoot);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(_artifactsRoot, "*.nupkg"))
        {
            if (file.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var pkg = ReadPackage(file);
                if (pkg == null) continue;
                _all.Add(pkg);
                if (!_byId.TryGetValue(pkg.Id, out var list))
                    _byId[pkg.Id] = list = new();
                list.Add(pkg);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to read {File}", file);
            }
        }

        foreach (var list in _byId.Values)
            list.Sort((a, b) => string.Compare(a.Version, b.Version, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<NuGetPackage> All => _all;

    public IReadOnlyList<NuGetPackage> GetVersions(string id) =>
        _byId.TryGetValue(id, out var list) ? list : Array.Empty<NuGetPackage>();

    public NuGetPackage? Get(string id, string version) =>
        GetVersions(id).FirstOrDefault(p =>
            string.Equals(p.Version, version, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> AllIds() => _byId.Keys.OrderBy(x => x).ToList();

    public IReadOnlyList<NuGetPackage> LatestPerId() =>
        _byId.Values.Select(v => v[^1]).OrderBy(p => p.Id).ToList();

    private static NuGetPackage? ReadPackage(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var nuspecEntry = zip.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
            !e.FullName.Contains('/') && !e.FullName.Contains('\\'));
        if (nuspecEntry == null) return null;

        using var stream = nuspecEntry.Open();
        var doc = XDocument.Load(stream);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;

        var meta = doc.Root?.Element(ns + "metadata");
        if (meta == null) return null;

        string? El(string name) => meta.Element(ns + name)?.Value;

        var id = El("id") ?? throw new InvalidOperationException("nuspec missing id");
        var version = El("version") ?? throw new InvalidOperationException("nuspec missing version");

        var depGroups = new List<NuGetDependencyGroup>();
        var dependenciesEl = meta.Element(ns + "dependencies");
        if (dependenciesEl != null)
        {
            var groupEls = dependenciesEl.Elements(ns + "group").ToList();
            if (groupEls.Count == 0)
            {
                depGroups.Add(new NuGetDependencyGroup
                {
                    Dependencies = dependenciesEl.Elements(ns + "dependency")
                        .Select(ParseDep).ToList()
                });
            }
            else
            {
                foreach (var g in groupEls)
                {
                    depGroups.Add(new NuGetDependencyGroup
                    {
                        TargetFramework = (string?)g.Attribute("targetFramework"),
                        Dependencies = g.Elements(ns + "dependency").Select(ParseDep).ToList()
                    });
                }
            }
        }

        var fi = new FileInfo(path);
        return new NuGetPackage
        {
            Id = id,
            Version = version,
            Description = El("description"),
            Authors = El("authors"),
            ProjectUrl = El("projectUrl"),
            LicenseExpression = meta.Element(ns + "license")?.Value,
            Tags = (El("tags") ?? "").Split(new[] { ' ', ';', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
            DependencyGroups = depGroups,
            NupkgPath = path,
            Published = fi.LastWriteTimeUtc,
            SizeBytes = fi.Length,
        };

        NuGetDependency ParseDep(XElement d) => new()
        {
            Id = (string?)d.Attribute("id") ?? "",
            Range = (string?)d.Attribute("version"),
        };
    }
}
