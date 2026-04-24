namespace SharpDocs.Models;

public sealed class NuGetPackage
{
    public required string Id { get; init; }
    public required string Version { get; init; }             // original/normalized
    public string? Description { get; init; }
    public string? Authors { get; init; }
    public string? ProjectUrl { get; init; }
    public string? LicenseExpression { get; init; }
    public List<string> Tags { get; init; } = new();
    public List<NuGetDependencyGroup> DependencyGroups { get; init; } = new();
    public required string NupkgPath { get; init; }
    public required DateTime Published { get; init; }         // file mtime
    public required long SizeBytes { get; init; }
}

public sealed class NuGetDependencyGroup
{
    public string? TargetFramework { get; init; }
    public List<NuGetDependency> Dependencies { get; init; } = new();
}

public sealed class NuGetDependency
{
    public required string Id { get; init; }
    public string? Range { get; init; }
}
