using System.Globalization;
using Microsoft.Extensions.Options;

namespace GitCandy.Help;

internal static class HelpContentEndpoint
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".css"] = "text/css; charset=utf-8",
            [".html"] = "text/html; charset=utf-8",
            [".ico"] = "image/x-icon",
            [".js"] = "text/javascript; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".png"] = "image/png",
            [".svg"] = "image/svg+xml",
            [".txt"] = "text/plain; charset=utf-8",
            [".webp"] = "image/webp",
            [".woff2"] = "font/woff2"
        };

    public static async Task ServeAsync(HttpContext context)
    {
        SetSecurityHeaders(context.Response.Headers);

        var routePath = context.Request.RouteValues["path"] as string;
        if (!TryResolvePath(context, routePath, out var filePath, out var relativePath)
            || !File.Exists(filePath))
        {
            await WriteNotFoundAsync(context);
            return;
        }

        var extension = Path.GetExtension(filePath);
        if (!ContentTypes.TryGetValue(extension, out var contentType))
        {
            await WriteNotFoundAsync(context);
            return;
        }

        var fileInfo = new FileInfo(filePath);
        var entityTag = $"\"{fileInfo.Length:x}-{fileInfo.LastWriteTimeUtc.Ticks:x}\"";
        context.Response.Headers.ETag = entityTag;
        context.Response.Headers.LastModified = fileInfo.LastWriteTimeUtc.ToString("R", CultureInfo.InvariantCulture);
        context.Response.Headers.CacheControl = GetCacheControl(relativePath);

        if (context.Request.Headers.IfNoneMatch.Any(value => string.Equals(value, entityTag, StringComparison.Ordinal)))
        {
            context.Response.StatusCode = StatusCodes.Status304NotModified;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.ContentLength = fileInfo.Length;
        if (HttpMethods.IsHead(context.Request.Method))
        {
            return;
        }

        await context.Response.SendFileAsync(filePath, context.RequestAborted);
    }

    public static Task ServeRootAsync(HttpContext context)
    {
        if (context.Request.Path.Value?.EndsWith("/", StringComparison.Ordinal) == true)
        {
            return ServeAsync(context);
        }

        var location = $"{context.Request.PathBase}/help/";
        context.Response.Redirect(location, permanent: true, preserveMethod: true);
        return Task.CompletedTask;
    }

    private static bool TryResolvePath(
        HttpContext context,
        string? routePath,
        out string filePath,
        out string relativePath)
    {
        filePath = string.Empty;
        relativePath = string.Empty;
        var requestedPath = routePath?.Trim('/') ?? string.Empty;
        if (requestedPath.IndexOf('\\') >= 0
            || requestedPath.IndexOf('\0') >= 0)
        {
            return false;
        }

        var segments = requestedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment is "." or ".." || segment.IndexOf(':') >= 0))
        {
            return false;
        }

        if (segments.Length == 0)
        {
            relativePath = "index.html";
        }
        else if (context.Request.Path.Value?.EndsWith("/", StringComparison.Ordinal) == true
            || string.IsNullOrEmpty(Path.GetExtension(segments[^1])))
        {
            relativePath = Path.Combine([.. segments, "index.html"]);
        }
        else
        {
            relativePath = Path.Combine(segments);
        }

        var options = context.RequestServices.GetRequiredService<IOptions<HelpContentOptions>>().Value;
        var configuredRoot = string.IsNullOrWhiteSpace(options.ContentPath)
            ? Path.Combine(AppContext.BaseDirectory, "wwwroot", "help")
            : options.ContentPath;
        var rootPath = Path.GetFullPath(configuredRoot, AppContext.BaseDirectory);
        var candidatePath = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var relativeCandidate = Path.GetRelativePath(rootPath, candidatePath);
        if (Path.IsPathRooted(relativeCandidate)
            || relativeCandidate.Equals("..", StringComparison.Ordinal)
            || relativeCandidate.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return false;
        }

        filePath = candidatePath;
        return true;
    }

    private static string GetCacheControl(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        if (normalizedPath.StartsWith("assets/", StringComparison.Ordinal)
            || normalizedPath.StartsWith("archive/", StringComparison.Ordinal))
        {
            return "public, max-age=86400";
        }

        return normalizedPath.Equals("index.html", StringComparison.Ordinal)
            || normalizedPath.Equals("help-manifest.json", StringComparison.Ordinal)
            || normalizedPath.Equals("search-index.json", StringComparison.Ordinal)
                ? "no-cache"
                : "public, max-age=300";
    }

    private static void SetSecurityHeaders(IHeaderDictionary headers)
    {
        headers.ContentSecurityPolicy = "default-src 'none'; style-src 'self'; script-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
    }

    private static async Task WriteNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.WriteAsync("Help page not found.", context.RequestAborted);
        }
    }
}
