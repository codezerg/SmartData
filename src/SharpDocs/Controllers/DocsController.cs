using Microsoft.AspNetCore.Mvc;
using SharpDocs.Services;

namespace SharpDocs.Controllers;

public sealed class DocsController : Controller
{
    private readonly DocsLoader _docs;
    public DocsController(DocsLoader docs) { _docs = docs; }

    // GET /  and  GET /{**slug}
    [HttpGet]
    public IActionResult Page(string? slug)
    {
        var key = (slug ?? "").Trim('/');
        if (string.IsNullOrEmpty(key)) key = "index";

        var page = _docs.Find(key);
        if (page == null) return NotFound();

        ViewData["Nav"] = _docs.Nav;
        ViewData["ActiveSlug"] = page.Slug;

        // htmx partial request → return just the article body
        if (Request.Headers.ContainsKey("HX-Request"))
            return PartialView("_Article", page);

        return View("Index", page);
    }
}
