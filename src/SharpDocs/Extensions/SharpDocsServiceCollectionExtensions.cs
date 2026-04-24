using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpDocs.Services;

namespace SharpDocs;

public static class SharpDocsServiceCollectionExtensions
{
    public static IServiceCollection AddSharpDocs(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "SharpDocs")
    {
        services.Configure<SharpDocsOptions>(configuration.GetSection(sectionName));

        services.AddSingleton<DocsLoader>();
        services.AddSingleton<SearchIndex>();
        services.AddSingleton<NuGetFeed>();

        services.AddControllersWithViews();
        return services;
    }
}
