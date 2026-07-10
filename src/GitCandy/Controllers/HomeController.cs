using System.Diagnostics;
using GitCandy.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace GitCandy.Controllers;

public sealed class HomeController : CandyControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public IActionResult Index()
    {
        return RedirectToStartPage();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult About()
    {
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Language(string? lang, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return BadRequest();
        }

        var cultureName = lang.ToLowerInvariant() switch
        {
            "zh-cn" or "zh-hans" => "zh-Hans",
            "en" or "en-us" => "en-US",
            "fr" or "fr-fr" => "fr-FR",
            _ => null
        };
        if (cultureName is null)
        {
            return BadRequest();
        }

        var requestCulture = new RequestCulture(cultureName);
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(requestCulture),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
        Response.Cookies.Append("Lang", cultureName, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        });

        return RedirectToStartPage(GetLocalReturnUrl(returnUrl));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private string? GetLocalReturnUrl(string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return returnUrl;
        }

        if (!Uri.TryCreate(Request.Headers.Referer, UriKind.Absolute, out var referer)
            || !string.Equals(referer.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return referer.PathAndQuery;
    }
}
