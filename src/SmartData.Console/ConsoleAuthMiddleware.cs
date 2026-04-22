using Microsoft.AspNetCore.Http;
using SmartData.Console.Controllers;
using SmartData.Console.Services;

namespace SmartData.Console;

internal class ConsoleAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _consolePath;
    private readonly string _loginPath;

    public ConsoleAuthMiddleware(RequestDelegate next, ConsoleRoutes routes)
    {
        _next = next;
        _consolePath = routes.Path();
        _loginPath = routes.Path("login");
    }

    public async Task InvokeAsync(HttpContext context, ConsoleAuthService auth)
    {
        var path = context.Request.Path.Value ?? "";

        if (!path.StartsWith(_consolePath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Allow login page and static assets through
        if (path.StartsWith(_loginPath, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_content/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var token = context.Request.Cookies[AuthController.CookieName];
        var session = auth.GetSession(token);

        if (session == null)
        {
            var returnUrl = Uri.EscapeDataString(path);
            context.Response.Redirect($"{_loginPath}?returnUrl={returnUrl}");
            return;
        }

        context.Items["ConsoleUser"] = session.Username;
        context.Items["ConsoleToken"] = session.ServerToken;
        await _next(context);
    }
}
