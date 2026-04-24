namespace SharpDocs.Models;

public sealed class SiteConfig
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Github { get; set; }
    public List<SidebarGroup> Sidebar { get; set; } = new();
}

public sealed class SidebarGroup
{
    public string Label { get; set; } = "";
    public List<SidebarItem> Items { get; set; } = new();
}

public sealed class SidebarItem
{
    public string Label { get; set; } = "";
    public string Slug { get; set; } = "";
}
