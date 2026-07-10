using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GitCandy.Controllers;

public abstract class CandyControllerBase : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var culture = CultureInfo.CurrentUICulture;
        ViewData["Language"] = culture.Name.StartsWith("en", StringComparison.OrdinalIgnoreCase)
            ? culture.NativeName
            : $"{culture.EnglishName} - {culture.NativeName}";
        ViewData["Lang"] = culture.Name;
        Response.Headers["X-GitCandy-Version"] = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";

        base.OnActionExecuting(context);
    }

    protected IActionResult RedirectToStartPage(string? returnUrl = null)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Repository");
    }
}
