using Microsoft.AspNetCore.Mvc;
using SharpDocs.Services;

namespace SharpDocs.Controllers;

[Route("search")]
public sealed class SearchController : Controller
{
    private readonly SearchIndex _index;
    public SearchController(SearchIndex index) { _index = index; }

    [HttpGet("")]
    public IActionResult Index(string? q)
    {
        var hits = string.IsNullOrWhiteSpace(q)
            ? Array.Empty<SearchHit>()
            : _index.Search(q).ToArray();

        ViewData["Query"] = q;

        if (Request.Headers.ContainsKey("HX-Request"))
            return PartialView("_Results", hits);

        return View(hits);
    }
}
