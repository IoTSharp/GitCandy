using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace GitCandy.Controllers;

public sealed class CompatibilityController : Controller
{
    public IActionResult Account(string? legacyAction = null, string? name = null)
    {
        return Placeholder("Account", legacyAction, defaultAction: "Login", name, path: null);
    }

    public IActionResult Team(string? legacyAction = null, string? name = null)
    {
        return Placeholder("Team", legacyAction, defaultAction: "Index", name, path: null);
    }

    public IActionResult Repository(string? legacyAction = null, string? name = null, string? path = null)
    {
        return Placeholder("Repository", legacyAction, defaultAction: "Index", name, path);
    }

    public IActionResult Setting(string? legacyAction = null)
    {
        return Placeholder("Setting", legacyAction, defaultAction: "Edit", name: null, path: null);
    }

    public IActionResult Git(string project, string? verb = null)
    {
        Response.Headers[HeaderNames.CacheControl] = "no-cache, max-age=0, must-revalidate";
        Response.Headers[HeaderNames.Pragma] = "no-cache";
        Response.Headers[HeaderNames.Expires] = "Fri, 01 Jan 1980 00:00:00 GMT";

        var target = string.IsNullOrWhiteSpace(verb)
            ? project
            : $"{project}/{verb}";

        return new ContentResult
        {
            StatusCode = StatusCodes.Status501NotImplemented,
            ContentType = "text/plain; charset=utf-8",
            Content = $"Git Smart HTTP placeholder for {target}.",
        };
    }

    private ContentResult Placeholder(
        string legacyController,
        string? legacyAction,
        string defaultAction,
        string? name,
        string? path)
    {
        var effectiveAction = string.IsNullOrWhiteSpace(legacyAction)
            ? defaultAction
            : legacyAction;
        var target = string.IsNullOrWhiteSpace(name)
            ? $"{legacyController}/{effectiveAction}"
            : $"{legacyController}/{effectiveAction}/{name}";

        if (!string.IsNullOrWhiteSpace(path))
        {
            target = $"{target}/{path}";
        }

        return Content(
            $"GitCandy ASP.NET Core compatibility placeholder for {target}.",
            "text/plain; charset=utf-8");
    }
}
