using Microsoft.AspNetCore.Mvc;

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
