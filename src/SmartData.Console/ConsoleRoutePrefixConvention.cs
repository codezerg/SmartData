using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace SmartData.Console;

/// <summary>
/// Rewrites controller route templates from /console/... to /{prefix}/... at startup.
/// Only touches controllers in the SmartData.Console assembly.
/// </summary>
internal class ConsoleRoutePrefixConvention : IApplicationModelConvention
{
    private const string DefaultPrefix = "/console";
    private readonly string _newPrefix;

    public ConsoleRoutePrefixConvention(string routePrefix)
    {
        _newPrefix = "/" + routePrefix.Trim('/');
    }

    public void Apply(ApplicationModel application)
    {
        var consoleAssembly = typeof(ConsoleRoutePrefixConvention).Assembly;

        foreach (var controller in application.Controllers)
        {
            if (controller.ControllerType.Assembly != consoleAssembly)
                continue;

            foreach (var action in controller.Actions)
            {
                foreach (var selector in action.Selectors)
                {
                    var routeModel = selector.AttributeRouteModel;
                    var template = routeModel?.Template;
                    if (template == null || routeModel == null)
                        continue;

                    if (string.Equals(template, DefaultPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        routeModel.Template = _newPrefix;
                    }
                    else if (template.StartsWith(DefaultPrefix + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        routeModel.Template = _newPrefix + template[DefaultPrefix.Length..];
                    }
                }
            }
        }
    }
}
