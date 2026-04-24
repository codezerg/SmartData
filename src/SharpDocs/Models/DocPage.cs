namespace SharpDocs.Models;

public sealed class DocPage
{
    public required string Slug { get; init; }           // e.g. "fundamentals/procedures"
    public required string Section { get; init; }        // parent folder, e.g. "fundamentals"; "" for root
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Html { get; init; }           // rendered body
    public required string RawText { get; init; }        // plain text for search snippets
    public required List<Heading> Headings { get; init; }
    public required string SourcePath { get; init; }
}

public sealed record Heading(int Level, string Text, string Id);

public sealed class NavNode
{
    public required string Label { get; init; }
    public string? Slug { get; init; }                   // null = section header
    public List<NavNode> Children { get; init; } = new();
}
