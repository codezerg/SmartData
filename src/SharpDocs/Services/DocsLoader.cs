using System.Text;
using System.Text.Json;
using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpDocs.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharpDocs.Services;

public sealed class DocsLoader
{
    public const string ConfigFileName = "sharpdocs.json";

    private readonly Dictionary<string, DocPage> _bySlug;
    private readonly List<NavNode> _nav;
    private readonly SiteConfig _config;
    private readonly MarkdownPipeline _pipeline;

    public DocsLoader(IOptions<SharpDocsOptions> options, IHostEnvironment env)
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoIdentifiers()
            .Build();

        var root = ResolveRoot(options.Value.DocsRoot, env.ContentRootPath);
        _bySlug = new(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Docs root not found: {root}");

        _config = LoadSiteConfig(root);

        foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            var page = LoadFile(root, file);
            if (page != null)
                _bySlug[page.Slug] = page;
        }

        _nav = BuildNav(_config.Sidebar, _bySlug);
    }

    public IReadOnlyCollection<DocPage> AllPages => _bySlug.Values;
    public IReadOnlyList<NavNode> Nav => _nav;
    public SiteConfig Config => _config;
    public DocPage? Find(string slug) =>
        _bySlug.TryGetValue(slug.Trim('/'), out var p) ? p : null;

    private static SiteConfig LoadSiteConfig(string root)
    {
        var path = Path.Combine(root, ConfigFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"SharpDocs requires a {ConfigFileName} at the docs root. Expected: {path}");

        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<SiteConfig>(json, opts)
            ?? throw new InvalidOperationException($"{ConfigFileName} is empty or invalid.");
        return config;
    }

    private DocPage? LoadFile(string root, string file)
    {
        var text = File.ReadAllText(file);
        var (frontmatter, body) = SplitFrontmatter(text);

        var rel = Path.GetRelativePath(root, file)
            .Replace('\\', '/');
        var slug = rel[..rel.LastIndexOf('.')]; // strip extension
        var lastSlash = slug.LastIndexOf('/');
        var section = lastSlash < 0 ? "" : slug[..lastSlash];
        if (slug.EndsWith("/index", StringComparison.OrdinalIgnoreCase))
            slug = slug[..^"/index".Length];

        var title = frontmatter.TryGetValue("title", out var t) ? t : Path.GetFileNameWithoutExtension(file);
        var desc = frontmatter.TryGetValue("description", out var d) ? d : null;

        var doc = Markdown.Parse(body, _pipeline);
        var html = doc.ToHtml(_pipeline);
        var rawText = ExtractText(doc);
        var headings = doc.Descendants<HeadingBlock>()
            .Where(h => h.Level >= 2 && h.Level <= 3)
            .Select(h =>
            {
                var text = InlineText(h.Inline);
                var id = h.TryGetAttributes()?.Id ?? "";
                return new Heading(h.Level, text, id);
            })
            .Where(h => !string.IsNullOrWhiteSpace(h.Text) && !string.IsNullOrWhiteSpace(h.Id))
            .ToList();

        return new DocPage
        {
            Slug = slug,
            Section = section,
            Title = title,
            Description = desc,
            Html = html,
            RawText = rawText,
            Headings = headings,
            SourcePath = file,
        };
    }

    private static (Dictionary<string, string> frontmatter, string body) SplitFrontmatter(string text)
    {
        var fm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!text.StartsWith("---"))
            return (fm, text);

        var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return (fm, text);

        var yamlBlock = text.Substring(3, end - 3).Trim('\n', '\r');
        var body = text[(end + 4)..].TrimStart('\r', '\n');

        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var obj = deserializer.Deserialize<Dictionary<string, object>>(yamlBlock);
            if (obj != null)
            {
                foreach (var kv in obj)
                {
                    if (kv.Value is string s)
                        fm[kv.Key] = s;
                }
            }
        }
        catch { /* bad yaml — ignore */ }

        return (fm, body);
    }

    private static string InlineText(ContainerInline? inline)
    {
        if (inline == null) return "";
        var sb = new StringBuilder();
        foreach (var child in inline)
        {
            if (child is LiteralInline lit) sb.Append(lit.Content.ToString());
            else if (child is CodeInline code) sb.Append(code.Content);
            else if (child is ContainerInline c) sb.Append(InlineText(c));
        }
        return sb.ToString();
    }

    private static string ExtractText(MarkdownDocument doc)
    {
        var sb = new StringBuilder();
        foreach (var block in doc.Descendants())
        {
            if (block is LeafBlock leaf && leaf.Inline != null)
            {
                sb.Append(InlineText(leaf.Inline));
                sb.Append(' ');
            }
        }
        return sb.ToString();
    }

    private static List<NavNode> BuildNav(List<SidebarGroup> sidebar, Dictionary<string, DocPage> bySlug)
    {
        var nav = new List<NavNode>();
        foreach (var group in sidebar)
        {
            var children = new List<NavNode>();
            foreach (var item in group.Items)
            {
                var slug = item.Slug.Trim('/');
                if (!bySlug.ContainsKey(slug))
                    throw new InvalidOperationException(
                        $"{ConfigFileName} references slug '{slug}' but no matching markdown page was found.");
                children.Add(new NavNode { Label = item.Label, Slug = slug });
            }
            if (children.Count > 0)
                nav.Add(new NavNode { Label = group.Label, Children = children });
        }
        return nav;
    }

    private static string ResolveRoot(string configured, string contentRoot)
    {
        if (Path.IsPathRooted(configured)) return Path.GetFullPath(configured);
        return Path.GetFullPath(Path.Combine(contentRoot, configured));
    }
}
