namespace SharpDocs.Services;

public sealed class SharpDocsOptions
{
    public string DocsRoot { get; set; } = "docs";
    public string ArtifactsRoot { get; set; } = "artifacts";
    public List<NavLink> NavLinks { get; set; } = new();
}

public sealed class NavLink
{
    public string Label { get; set; } = "";
    public string Href { get; set; } = "";
}
