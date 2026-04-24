using Microsoft.AspNetCore.Mvc;
using SharpDocs.Services;

namespace SharpDocs.Controllers;

[Route("packages")]
public sealed class PackagesController : Controller
{
    private readonly NuGetFeed _feed;
    public PackagesController(NuGetFeed feed) { _feed = feed; }

    [HttpGet("")]
    public IActionResult Index() => View(_feed.LatestPerId());

    [HttpGet("{id}")]
    public IActionResult Detail(string id)
    {
        var versions = _feed.GetVersions(id);
        if (versions.Count == 0) return NotFound();
        ViewData["Id"] = id;
        return View(versions);
    }
}
