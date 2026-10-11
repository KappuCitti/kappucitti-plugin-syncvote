using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using KappuCitti.Plugin.SyncVote.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace KappuCitti.Plugin.SyncVote.Infrastructure;

/// <summary>
/// Adds the SyncVote stylesheet and script to the web client's <c>index.html</c> while it is served,
/// so neither a modified jellyfin-web nor another plugin is needed.
/// </summary>
public class WebClientInjectionMiddleware
{
    private const string Marker = "SyncVote/Client/syncvote.js";

    private readonly RequestDelegate _next;
    private readonly IPluginSettings _settings;
    private readonly ILogger<WebClientInjectionMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebClientInjectionMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="settings">The plugin settings.</param>
    /// <param name="logger">The logger.</param>
    public WebClientInjectionMiddleware(RequestDelegate next, IPluginSettings settings, ILogger<WebClientInjectionMiddleware> logger)
    {
        _next = next;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Handles a request.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsIndexRequest(context, out var basePath) || !_settings.Configuration.InjectWebClient)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        // Ask for an uncompressed page: the body is rewritten here.
        context.Request.Headers.Remove(HeaderNames.AcceptEncoding);

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context).ConfigureAwait(false);

            buffer.Position = 0;
            var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
                         && (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) ?? false);
            if (!isHtml)
            {
                await CopyAsync(buffer, originalBody, context).ConfigureAwait(false);
                return;
            }

            var html = Encoding.UTF8.GetString(buffer.ToArray());
            var injected = Inject(html, basePath);
            var bytes = Encoding.UTF8.GetBytes(injected);

            context.Response.Headers.Remove(HeaderNames.ContentEncoding);
            context.Response.Headers.Remove(HeaderNames.LastModified);
            context.Response.Headers[HeaderNames.ETag] = "\"" + Convert.ToHexString(SHA256.HashData(bytes), 0, 8).ToLowerInvariant() + "\"";
            context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
            context.Response.ContentLength = bytes.Length;

            if (context.Request.Headers.IfNoneMatch == context.Response.Headers[HeaderNames.ETag])
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = 0;
                return;
            }

            await originalBody.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to add SyncVote to the web client page");
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    internal static string Inject(string html, string basePath)
    {
        if (html.Contains(Marker, StringComparison.Ordinal))
        {
            return html;
        }

        var tags = $"<link rel=\"stylesheet\" href=\"{basePath}/SyncVote/Client/syncvote.css\">" +
                   $"<script defer src=\"{basePath}/{Marker}\"></script>";

        var index = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return index < 0 ? html + tags : html.Insert(index, tags);
    }

    // The page is /web/ (or /web/index.html); Jellyfin may be hosted under a base path such as /jellyfin.
    private static bool IsIndexRequest(HttpContext context, out string basePath)
    {
        basePath = string.Empty;
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return false;
        }

        var path = context.Request.Path.Value ?? string.Empty;
        var web = path.IndexOf("/web", StringComparison.OrdinalIgnoreCase);
        if (web < 0)
        {
            return false;
        }

        var rest = path.Substring(web + 4);
        if (rest is not ("" or "/" or "/index.html"))
        {
            return false;
        }

        basePath = path.Substring(0, web);
        return true;
    }

    private static async Task CopyAsync(Stream source, Stream destination, HttpContext context)
    {
        context.Response.Body = destination;
        await source.CopyToAsync(destination).ConfigureAwait(false);
    }
}
