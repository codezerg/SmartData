using Microsoft.AspNetCore.Mvc;
using SmartData.Console.Services;

namespace SmartData.Console.Controllers;

public class AuthController : Controller
{
    private readonly ConsoleAuthService _auth;
    private readonly ConsoleRoutes _routes;
    public const string CookieName = "sd_console_token";

    public AuthController(ConsoleAuthService auth, ConsoleRoutes routes)
    {
        _auth = auth;
        _routes = routes;
    }

    [HttpGet("/console/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        var token = Request.Cookies[CookieName];
        if (_auth.GetUsername(token) != null)
            return Redirect(returnUrl ?? _routes.Path());

        ViewData["Error"] = TempData["Error"];
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("/console/login")]
    public async Task<IActionResult> LoginPost([FromForm] string? username, [FromForm] string? password, [FromForm] string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            TempData["Error"] = "Username and password are required.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        var token = await _auth.LoginAsync(username, password);
        if (token == null)
        {
            TempData["Error"] = "Invalid username or password.";
            return RedirectToAction(nameof(Login), new { returnUrl });
        }

        Response.Cookies.Append(CookieName, token, new Microsoft.AspNetCore.Http.CookieOptions
        {
            HttpOnly = true,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            IsEssential = true
        });

        return Redirect(returnUrl ?? _routes.Path());
    }

    [HttpPost("/console/logout")]
    public async Task<IActionResult> Logout()
    {
        var token = Request.Cookies[CookieName];
        await _auth.LogoutAsync(token);
        Response.Cookies.Delete(CookieName);
        return Redirect(_routes.Path("login"));
    }
}
